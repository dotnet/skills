package main

import (
	"errors"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
)

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
