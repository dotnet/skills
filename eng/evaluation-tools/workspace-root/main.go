package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"syscall"
	"time"
)

const protocol = 1

type request struct {
	Protocol  int     `json:"protocol"`
	Operation string  `json:"operation"`
	Path      string  `json:"path"`
	Dest      string  `json:"dest,omitempty"`
	Content   string  `json:"content,omitempty"`
	Mode      *uint32 `json:"mode,omitempty"`
	Recursive bool    `json:"recursive,omitempty"`
	Force     bool    `json:"force,omitempty"`
	Node      string  `json:"node,omitempty"`
}

type failure struct {
	Message string `json:"message"`
	Errno   int    `json:"errno,omitempty"`
}

type response struct {
	Protocol int      `json:"protocol"`
	Result   any      `json:"result"`
	Error    *failure `json:"error,omitempty"`
}

func supported() bool {
	return (runtime.GOOS == "linux" || runtime.GOOS == "darwin") &&
		(runtime.GOARCH == "amd64" || runtime.GOARCH == "arm64")
}

func mode(req request, fallback fs.FileMode) fs.FileMode {
	if req.Mode == nil {
		return fallback
	}
	raw := *req.Mode
	result := fs.FileMode(raw & 0o777)
	if raw&0o4000 != 0 {
		result |= fs.ModeSetuid
	}
	if raw&0o2000 != 0 {
		result |= fs.ModeSetgid
	}
	if raw&0o1000 != 0 {
		result |= fs.ModeSticky
	}
	return result
}

func execute(root *os.Root, req request) (any, error) {
	if req.Protocol != protocol || !filepath.IsLocal(req.Path) {
		return nil, errors.New("invalid rooted filesystem request")
	}
	switch req.Operation {
	case "readFile":
		data, err := root.ReadFile(req.Path)
		return string(data), err
	case "writeFile", "appendFile":
		if err := root.MkdirAll(filepath.Dir(req.Path), 0o777); err != nil {
			return nil, err
		}
		flags := os.O_WRONLY | os.O_CREATE | os.O_TRUNC
		if req.Operation == "appendFile" {
			flags = os.O_WRONLY | os.O_CREATE | os.O_APPEND
		}
		file, err := root.OpenFile(req.Path, flags, mode(req, 0o666))
		if err != nil {
			return nil, err
		}
		_, err = io.WriteString(file, req.Content)
		return nil, errors.Join(err, file.Close())
	case "stat":
		if !filepath.IsAbs(req.Node) {
			return nil, errors.New("metadata requires an absolute Node executable")
		}
		// Pass an already-rooted descriptor, never a pathname, to Node for its
		// exact SDK timestamps (including platform-specific birthtime).
		file, err := root.OpenFile(req.Path, os.O_RDONLY|syscall.O_NONBLOCK, 0)
		if err != nil {
			return nil, err
		}
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		cmd := exec.CommandContext(ctx, req.Node, "--input-type=module", "-e", `
import { fstatSync } from 'node:fs';
const s = fstatSync(3);
console.log(JSON.stringify({
  isFile: s.isFile(), isDirectory: s.isDirectory(), size: s.size,
  mtime: s.mtime.toISOString(), birthtime: s.birthtime.toISOString()
}));
`)
		cmd.ExtraFiles = []*os.File{file}
		cmd.Env = []string{}
		data, err := cmd.Output()
		if ctx.Err() != nil {
			err = errors.Join(err, ctx.Err())
		}
		err = errors.Join(err, file.Close())
		if err != nil {
			return nil, fmt.Errorf("descriptor metadata: %w", err)
		}
		var result map[string]any
		if err := json.Unmarshal(data, &result); err != nil {
			return nil, err
		}
		return result, nil
	case "exists":
		_, err := root.Stat(req.Path)
		if errors.Is(err, fs.ErrNotExist) {
			return false, nil
		}
		return err == nil, err
	case "readdir", "readdirWithTypes":
		entries, err := fs.ReadDir(root.FS(), filepath.ToSlash(req.Path))
		if err != nil {
			return nil, err
		}
		if req.Operation == "readdir" {
			result := make([]string, 0, len(entries))
			for _, entry := range entries {
				result = append(result, entry.Name())
			}
			return result, nil
		}
		type entry struct {
			Name string `json:"name"`
			Type string `json:"type"`
		}
		result := make([]entry, 0, len(entries))
		for _, item := range entries {
			kind := "file"
			if item.IsDir() {
				kind = "directory"
			}
			result = append(result, entry{item.Name(), kind})
		}
		return result, nil
	case "mkdir":
		if req.Recursive {
			return nil, root.MkdirAll(req.Path, mode(req, 0o777))
		}
		return nil, root.Mkdir(req.Path, mode(req, 0o777))
	case "rm":
		if filepath.Clean(req.Path) == "." {
			return nil, errors.New("cannot remove the workspace root")
		}
		info, err := root.Lstat(req.Path)
		if errors.Is(err, fs.ErrNotExist) && req.Force {
			return nil, nil
		}
		if err != nil {
			return nil, err
		}
		if req.Recursive {
			return nil, root.RemoveAll(req.Path)
		}
		if info.IsDir() {
			return nil, &os.PathError{Op: "rm", Path: req.Path, Err: syscall.EISDIR}
		}
		err = root.Remove(req.Path)
		if req.Force && errors.Is(err, fs.ErrNotExist) {
			return nil, nil
		}
		return nil, err
	case "rename":
		if !filepath.IsLocal(req.Dest) || filepath.Clean(req.Path) == "." ||
			filepath.Clean(req.Dest) == "." {
			return nil, errors.New("cannot rename outside the root or rename the workspace root")
		}
		if err := root.MkdirAll(filepath.Dir(req.Dest), 0o777); err != nil {
			return nil, err
		}
		return nil, root.Rename(req.Path, req.Dest)
	default:
		return nil, fmt.Errorf("unsupported rooted filesystem operation %q", req.Operation)
	}
}

func run() (any, error) {
	if !supported() || runtime.Version() != "go1.27.1" {
		return nil, errors.New("rooted filesystem helper requires Go1.27.1 on Linux/macOS amd64/arm64")
	}
	var req request
	input := &io.LimitedReader{R: os.Stdin, N: (64 << 20) + 1}
	decoder := json.NewDecoder(input)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&req); err != nil {
		return nil, err
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		return nil, errors.New("expected exactly one filesystem request")
	}
	if input.N == 0 {
		return nil, errors.New("rooted filesystem request exceeds 64 MiB")
	}
	name := "/proc/self/fd/3"
	if runtime.GOOS == "darwin" {
		name = "/dev/fd/3"
	}
	root, err := os.OpenRoot(name)
	if err != nil {
		return nil, err
	}
	result, err := execute(root, req)
	return result, errors.Join(err, root.Close())
}

func main() {
	result, err := run()
	out := response{Protocol: protocol, Result: result}
	if err != nil {
		out.Error = &failure{Message: err.Error()}
		var errno syscall.Errno
		if errors.As(err, &errno) {
			out.Error.Errno = int(errno)
		}
	}
	if err := json.NewEncoder(os.Stdout).Encode(out); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
