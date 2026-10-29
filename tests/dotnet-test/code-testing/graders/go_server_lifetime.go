// Instrument a replay copy, never the delivered Go fixture.
package main

import (
	"bytes"
	"fmt"
	"go/ast"
	"go/format"
	"go/parser"
	"go/scanner"
	"go/token"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

type rewriter struct {
	owner    *ast.Ident
	imports  map[string]string
	seen     map[*ast.CallExpr]bool
	files    *token.FileSet
	count    *int
	problems *[]error
}

func (v rewriter) packageMember(expr ast.Expr, pkg, member string) bool {
	if selector, ok := expr.(*ast.SelectorExpr); ok {
		id, ok := selector.X.(*ast.Ident)
		return ok && id.Obj == nil && v.imports[id.Name] == pkg && selector.Sel.Name == member
	}
	id, ok := expr.(*ast.Ident)
	return ok && id.Obj == nil && v.imports["."] == pkg && id.Name == member
}

func (v rewriter) testParameter(params *ast.FieldList) *ast.Ident {
	if params == nil {
		return nil
	}
	for _, field := range params.List {
		typ := field.Type
		if pointer, ok := typ.(*ast.StarExpr); ok {
			typ = pointer.X
		}
		for _, name := range []string{"T", "B", "F", "TB"} {
			if v.packageMember(typ, "testing", name) && len(field.Names) == 1 {
				return field.Names[0]
			}
		}
	}
	return nil
}

func (v rewriter) Visit(node ast.Node) ast.Visitor {
	switch node := node.(type) {
	case *ast.FuncDecl:
		v.owner = v.testParameter(node.Type.Params)
		v.enter(node.Body)
	case *ast.FuncLit:
		if owner := v.testParameter(node.Type.Params); owner != nil {
			v.owner = owner
			v.enter(node.Body)
		}
	case *ast.CallExpr:
		if v.seen[node] {
			break
		}
		selector, selected := node.Fun.(*ast.SelectorExpr)
		ownedTemp := false
		if selected && v.owner != nil {
			id, ok := selector.X.(*ast.Ident)
			ownedTemp = ok && id.Obj == v.owner.Obj && selector.Sel.Name == "TempDir"
		}
		mkdir := v.packageMember(node.Fun, "os", "MkdirTemp")
		if ownedTemp || mkdir {
			if v.owner == nil {
				*v.problems = append(*v.problems, fmt.Errorf("%s: temporary directory has no testing owner", v.files.Position(node.Pos())))
				return nil
			}
			inner := *node
			v.seen[&inner] = true
			if mkdir {
				node.Fun = ast.NewIdent("evalLifetimeMkdirTemp")
				node.Args = append([]ast.Expr{ast.NewIdent(v.owner.Name)}, node.Args...)
			} else {
				*node = ast.CallExpr{Fun: ast.NewIdent("evalLifetimeTempDir"),
					Args: []ast.Expr{ast.NewIdent(v.owner.Name), &inner}}
			}
			*v.count++
			break
		}
		if id, ok := node.Fun.(*ast.Ident); ok && id.Name == "Download" && len(node.Args) == 3 {
			if v.owner == nil {
				*v.problems = append(*v.problems, fmt.Errorf("%s: Download has no testing owner", v.files.Position(node.Pos())))
				return nil
			}
			node.Args[2] = &ast.CallExpr{Fun: ast.NewIdent("evalLifetimeDestination"),
				Args: []ast.Expr{ast.NewIdent(v.owner.Name), node.Args[2]}}
			*v.count++
			break
		}
		for _, factory := range []string{"NewServer", "NewTLSServer", "NewUnstartedServer"} {
			if !v.packageMember(node.Fun, "net/http/httptest", factory) {
				continue
			}
			if v.owner == nil {
				*v.problems = append(*v.problems, fmt.Errorf(
					"%s: server factory has no testing.T/TB owner; this fixture probe requires an owner parameter",
					v.files.Position(node.Pos())))
				return nil
			}
			inner := *node
			if selector, ok := inner.Fun.(*ast.SelectorExpr); ok {
				inner.Fun = &ast.SelectorExpr{X: selector.X, Sel: ast.NewIdent("NewUnstartedServer")}
			} else {
				inner.Fun = ast.NewIdent("NewUnstartedServer")
			}
			v.seen[&inner] = true
			*node = ast.CallExpr{
				Fun: ast.NewIdent("evalLifetimeServer"),
				Args: []ast.Expr{
					ast.NewIdent(v.owner.Name), &inner,
					&ast.BasicLit{Kind: token.STRING, Value: strconv.Quote(factory)},
					&ast.BasicLit{Kind: token.STRING, Value: strconv.Quote(v.files.Position(node.Pos()).String())},
				},
			}
			*v.count++
			break
		}
	}
	return v
}

func (v rewriter) enter(body *ast.BlockStmt) {
	if body != nil && v.owner != nil {
		body.List = append([]ast.Stmt{&ast.ExprStmt{X: &ast.CallExpr{
			Fun: ast.NewIdent("evalLifetimeOwner"), Args: []ast.Expr{ast.NewIdent(v.owner.Name)},
		}}}, body.List...)
		*v.count++
	}
}

const probe = `
package %s

import (
    "net"
    "net/http/httptest"
    "os"
    "path/filepath"
    "strings"
    "sync"
    "sync/atomic"
    "testing"
)

type evalLifetimeListener struct {
    net.Listener
    closed atomic.Bool
}

func (l *evalLifetimeListener) Close() error {
    err := l.Listener.Close()
    l.closed.Store(true)
    return err
}

type evalLifetimeState struct {
    sync.Mutex
    listeners map[*evalLifetimeListener]*httptest.Server
    locations map[*evalLifetimeListener]string
    directories []string
}

var evalLifetimeOwners sync.Map

func evalLifetimeOwner(t testing.TB) *evalLifetimeState {
    state := &evalLifetimeState{
        listeners: make(map[*evalLifetimeListener]*httptest.Server),
        locations: make(map[*evalLifetimeListener]string),
    }
    actual, loaded := evalLifetimeOwners.LoadOrStore(t, state)
    if loaded { return actual.(*evalLifetimeState) }
    // Registered at owner entry, so this runs after user cleanup (LIFO).
    t.Cleanup(func() {
        defer evalLifetimeOwners.Delete(t)
        state.Lock()
        defer state.Unlock()
        for listener, server := range state.listeners {
            if !listener.closed.Load() {
                t.Errorf("GO_SERVER_LIFETIME: unclosed server at %%s", state.locations[listener])
                server.Close()
            } else {
                t.Logf("GO_SERVER_LIFETIME: verified %%s", state.locations[listener])
            }
        }
        for _, directory := range state.directories {
            if _, err := os.Lstat(directory); !os.IsNotExist(err) {
                t.Errorf("GO_DIRECTORY_LIFETIME: directory not cleaned: %%s (%%v)", directory, err)
            }
        }
    })
    return state
}

func evalLifetimeTempDir(t testing.TB, directory string) string {
    t.Helper()
    absolute, err := filepath.EvalSymlinks(directory)
    if err != nil { t.Fatal(err) }
    absolute, err = filepath.Abs(absolute)
    if err != nil { t.Fatal(err) }
    state := evalLifetimeOwner(t)
    state.Lock()
    state.directories = append(state.directories, absolute)
    state.Unlock()
    return directory
}

func evalLifetimeMkdirTemp(t testing.TB, directory, pattern string) (string, error) {
    result, err := os.MkdirTemp(directory, pattern)
    if err == nil { evalLifetimeTempDir(t, result) }
    return result, err
}

func evalLifetimeDestination(t testing.TB, destination string) string {
    t.Helper()
    parent, err := filepath.EvalSymlinks(filepath.Dir(destination))
    if err != nil { t.Fatalf("GO_DIRECTORY_LIFETIME: unresolved destination parent: %%v", err) }
    parent, err = filepath.Abs(parent)
    if err != nil { t.Fatal(err) }
    owned := false
    evalLifetimeOwners.Range(func(_, value any) bool {
        state := value.(*evalLifetimeState)
        state.Lock()
        defer state.Unlock()
        for _, directory := range state.directories {
            relative, err := filepath.Rel(directory, parent)
            if err == nil && relative != ".." && !strings.HasPrefix(relative, ".."+string(filepath.Separator)) && !filepath.IsAbs(relative) {
                owned = true
                return false
            }
        }
        return true
    })
    if !owned { t.Fatalf("GO_DIRECTORY_LIFETIME: destination is outside owned temporary directories: %%s", destination) }
    t.Log("GO_DIRECTORY_LIFETIME: verified destination")
    return destination
}

func evalLifetimeServer(t testing.TB, server *httptest.Server, factory, location string) *httptest.Server {
    t.Helper()
    listener := &evalLifetimeListener{Listener: server.Listener}
    server.Listener = listener
    state := evalLifetimeOwner(t)
    state.Lock()
    state.listeners[listener] = server
    state.locations[listener] = location
    state.Unlock()
    switch factory {
    case "NewServer":
        server.Start()
    case "NewTLSServer":
        server.StartTLS()
    }
    return server
}
`

func run(root string) error {
	files, err := filepath.Glob(filepath.Join(root, "*_test.go"))
	if err != nil {
		return err
	}
	count := 0
	packages := map[string]bool{}
	for _, filename := range files {
		fset := token.NewFileSet()
		source, err := os.ReadFile(filename)
		if err != nil {
			return err
		}
		source = bytes.ReplaceAll(source, []byte("\r\n"), []byte("\n"))
		// Ordinary comment positions become invalid when adding AST wrappers.
		// Keep compiler/build directives; comments do not prove resource ownership.
		var scan scanner.Scanner
		positions := token.NewFileSet()
		file := positions.AddFile(filename, -1, len(source))
		scan.Init(file, source, nil, scanner.ScanComments)
		for {
			pos, kind, text := scan.Scan()
			if kind == token.EOF {
				break
			}
			if kind == token.COMMENT && !strings.HasPrefix(text, "//go:") && !strings.HasPrefix(text, "// +build") {
				start := file.Offset(pos)
				for i := start; i < start+len(text); i++ {
					if source[i] != '\n' && source[i] != '\r' {
						source[i] = ' '
					}
				}
			}
		}
		tree, err := parser.ParseFile(fset, filename, source, parser.ParseComments)
		if err != nil {
			return err
		}
		imports := map[string]string{}
		for _, spec := range tree.Imports {
			pkg, err := strconv.Unquote(spec.Path.Value)
			if err != nil {
				return err
			}
			name := filepath.Base(pkg)
			if spec.Name != nil {
				name = spec.Name.Name
			}
			imports[name] = pkg
		}
		before := count
		var problems []error
		ast.Walk(rewriter{
			imports: imports, seen: map[*ast.CallExpr]bool{},
			files: fset, count: &count, problems: &problems,
		}, tree)
		if len(problems) != 0 {
			return problems[0]
		}
		if before == count {
			continue
		}
		packages[tree.Name.Name] = true
		var rewritten bytes.Buffer
		if err := format.Node(&rewritten, fset, tree); err != nil {
			return err
		}
		if err := os.WriteFile(filename, rewritten.Bytes(), 0600); err != nil {
			return err
		}
	}
	if count == 0 {
		return fmt.Errorf("no owned httptest server factories found in the Go tests")
	}
	for pkg := range packages {
		filename := filepath.Join(root, "zz_eval_lifetime_"+pkg+"_test.go")
		data, err := format.Source([]byte(fmt.Sprintf(probe, pkg)))
		if err != nil {
			return err
		}
		out, err := os.OpenFile(filename, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
		if err != nil {
			return err
		}
		_, writeErr := out.Write(data)
		closeErr := out.Close()
		if writeErr != nil {
			return writeErr
		}
		if closeErr != nil {
			return closeErr
		}
	}
	fmt.Printf("GO_RESOURCE_LIFETIME: instrumented %d ownership sites\n", count)
	return nil
}

func main() {
	if len(os.Args) != 2 || strings.TrimSpace(os.Args[1]) == "" {
		fmt.Fprintln(os.Stderr, "usage: go_server_lifetime <replay-directory>")
		os.Exit(2)
	}
	if err := run(os.Args[1]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
