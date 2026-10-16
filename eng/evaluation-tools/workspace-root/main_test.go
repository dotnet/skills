package main

import (
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"syscall"
	"testing"
)

func TestFailedReplacementPreservesTarget(t *testing.T) {
	for _, existing := range []bool{false, true} {
		for _, short := range []bool{false, true} {
			t.Run(fmt.Sprintf("existing=%t/short=%t", existing, short), func(t *testing.T) {
				directory := t.TempDir()
				root, err := os.OpenRoot(directory)
				if err != nil {
					t.Fatal(err)
				}
				defer root.Close()
				if existing {
					if err := root.WriteFile("target", []byte("original"), 0o640); err != nil {
						t.Fatal(err)
					}
				}
				err = replaceFile(root, request{Path: "target", Content: "replacement"},
					func(writer io.Writer, content string) (int, error) {
						n, err := io.WriteString(writer, content[:3])
						if err != nil {
							return n, err
						}
						if short {
							return n, nil
						}
						return n, syscall.ENOSPC
					})
				expected := error(syscall.ENOSPC)
				if short {
					expected = io.ErrShortWrite
				}
				if !errors.Is(err, expected) {
					t.Fatalf("wrong write failure: %v", err)
				}
				data, readErr := root.ReadFile("target")
				if existing && (readErr != nil || string(data) != "original") {
					t.Fatalf("failed write changed target: %q, %v", data, readErr)
				}
				if !existing && !errors.Is(readErr, fs.ErrNotExist) {
					t.Fatalf("failed write created target: %q, %v", data, readErr)
				}
				entries, err := os.ReadDir(directory)
				count := 0
				if existing {
					count = 1
				}
				if err != nil || len(entries) != count {
					t.Fatalf("temporary output leaked: %v, %v", entries, err)
				}
			})
		}
	}
}

func TestReplacementPreservesModeAndRejectsSymlinks(t *testing.T) {
	directory := t.TempDir()
	root, err := os.OpenRoot(directory)
	if err != nil {
		t.Fatal(err)
	}
	defer root.Close()
	if err := root.WriteFile("target", []byte("original"), 0o640); err != nil {
		t.Fatal(err)
	}
	if _, err := execute(root, request{Protocol: protocol, Operation: "writeFile",
		Path: "target", Content: "replacement"}); err != nil {
		t.Fatal(err)
	}
	info, err := root.Stat("target")
	if err != nil || info.Mode().Perm() != 0o640 {
		t.Fatalf("replacement changed permissions: %v, %v", info, err)
	}
	if err := os.Symlink("target", filepath.Join(directory, "link")); err != nil {
		t.Fatal(err)
	}
	if _, err := execute(root, request{Protocol: protocol, Operation: "writeFile",
		Path: "link", Content: "blocked"}); err == nil {
		t.Fatal("symlink replacement did not reject")
	}
	data, err := root.ReadFile("target")
	if err != nil || string(data) != "replacement" {
		t.Fatalf("rejected symlink write changed destination: %q, %v", data, err)
	}
}

func TestReplacementRequiresExistingTargetWriteAccess(t *testing.T) {
	directory := t.TempDir()
	root, err := os.OpenRoot(directory)
	if err != nil {
		t.Fatal(err)
	}
	defer root.Close()
	if err := root.WriteFile("protected", []byte("original"), 0o444); err != nil {
		t.Fatal(err)
	}
	access, err := root.OpenFile("protected", os.O_WRONLY, 0)
	if err == nil {
		access.Close()
		t.Skip("host credentials permit writing the read-only target")
	}
	if !errors.Is(err, fs.ErrPermission) {
		t.Fatalf("expected kernel permission denial: %v", err)
	}
	_, err = execute(root, request{Protocol: protocol, Operation: "writeFile",
		Path: "protected", Content: "blocked"})
	if !errors.Is(err, fs.ErrPermission) {
		t.Fatalf("replacement bypassed target write access: %v", err)
	}
	data, err := root.ReadFile("protected")
	if err != nil || string(data) != "original" {
		t.Fatalf("rejected replacement changed target: %q, %v", data, err)
	}
	entries, err := os.ReadDir(directory)
	if err != nil || len(entries) != 1 {
		t.Fatalf("rejected replacement leaked output: %v, %v", entries, err)
	}
}

func TestOutsideSymlinkRejectedForEveryOperation(t *testing.T) {
	node, err := exec.LookPath("node")
	if err != nil {
		t.Fatal(err)
	}
	for _, operation := range []string{
		"readFile", "writeFile", "appendFile", "stat", "exists",
		"readdir", "readdirWithTypes", "mkdir", "rm", "rename",
	} {
		t.Run(operation, func(t *testing.T) {
			base := t.TempDir()
			workspace := filepath.Join(base, "workspace")
			outside := filepath.Join(base, "outside")
			for _, name := range []string{workspace, outside} {
				if err := os.Mkdir(name, 0o700); err != nil {
					t.Fatal(err)
				}
			}
			sentinel := filepath.Join(outside, "owned.txt")
			if err := os.WriteFile(sentinel, []byte("outside"), 0o600); err != nil {
				t.Fatal(err)
			}
			root, err := os.OpenRoot(workspace)
			if err != nil {
				t.Fatal(err)
			}
			defer root.Close()
			if err := os.Symlink(outside, filepath.Join(workspace, "parent")); err != nil {
				t.Fatal(err)
			}
			name := "parent/owned.txt"
			if operation == "readdir" || operation == "readdirWithTypes" {
				name = "parent"
			}
			if operation == "mkdir" {
				name = "parent/new"
			}
			_, err = execute(root, request{
				Protocol: protocol, Operation: operation, Path: name,
				Dest: "parent/renamed", Content: "changed", Recursive: true,
				Force: true, Node: node,
			})
			if err == nil {
				t.Fatal("outside symlink operation succeeded")
			}
			data, err := os.ReadFile(sentinel)
			if err != nil || string(data) != "outside" {
				t.Fatalf("outside sentinel changed: %q, %v", data, err)
			}
			entries, err := os.ReadDir(outside)
			if err != nil || len(entries) != 1 {
				t.Fatalf("outside directory changed: %v, %v", entries, err)
			}
		})
	}
}

func TestRootCapabilitySurvivesPathReplacement(t *testing.T) {
	base := t.TempDir()
	workspace := filepath.Join(base, "workspace")
	outside := filepath.Join(base, "outside")
	for _, name := range []string{workspace, outside} {
		if err := os.Mkdir(name, 0o700); err != nil {
			t.Fatal(err)
		}
	}
	if err := os.WriteFile(filepath.Join(workspace, "owned.txt"), []byte("inside"), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(outside, "owned.txt"), []byte("outside"), 0o600); err != nil {
		t.Fatal(err)
	}
	root, err := os.OpenRoot(workspace)
	if err != nil {
		t.Fatal(err)
	}
	defer root.Close()
	if err := os.Rename(workspace, filepath.Join(base, "saved")); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(outside, workspace); err != nil {
		t.Fatal(err)
	}
	result, err := execute(root, request{Protocol: protocol, Operation: "readFile", Path: "owned.txt"})
	if err != nil || result != "inside" {
		t.Fatalf("replaced pathname changed capability: %v, %v", result, err)
	}
}

func TestMissingAndRootMutations(t *testing.T) {
	root, err := os.OpenRoot(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer root.Close()
	result, err := execute(root, request{Protocol: protocol, Operation: "exists", Path: "missing"})
	if err != nil || result != false {
		t.Fatalf("missing existence: %v, %v", result, err)
	}
	_, err = execute(root, request{Protocol: protocol, Operation: "rm", Path: "missing"})
	if !errors.Is(err, fs.ErrNotExist) {
		t.Fatalf("missing remove must fail without force: %v", err)
	}
	_, err = execute(root, request{Protocol: protocol, Operation: "rm", Path: "missing", Force: true})
	if err != nil {
		t.Fatal(err)
	}
	for _, req := range []request{
		{Operation: "rm", Path: ".", Recursive: true, Force: true},
		{Operation: "rename", Path: ".", Dest: "new"},
		{Operation: "rename", Path: "file", Dest: "../outside"},
		{Operation: "readFile", Path: "../outside"},
		{Operation: "unknown", Path: "."},
	} {
		req.Protocol = protocol
		if _, err := execute(root, req); err == nil {
			t.Fatalf("invalid operation succeeded: %+v", req)
		}
	}
}
