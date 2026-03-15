I have the following C header for a Windows library targeting .NET Framework (x86):

```c
int32_t compress_buffer(const uint8_t* input, size_t input_len,
                        uint8_t* output, size_t output_len,
                        size_t* bytes_written);
```

Write a C# class named NativeCompression that exposes a P/Invoke
to call this function from a shared library called "compresslib".
