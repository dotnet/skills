# C++ Extension

Language-specific guidance for C++ test generation.

## Testing Internals

If types are not well suited for testing only through their public surface, consider exposing internals to tests using a preprocessor-guarded `friend` declaration:

```cpp
class MyClass {
#ifdef UNIT_TESTING
    friend class MyClassTest;
#endif
    // ...
};
```

Define `UNIT_TESTING` only in the test build configuration so production builds remain unaffected.
