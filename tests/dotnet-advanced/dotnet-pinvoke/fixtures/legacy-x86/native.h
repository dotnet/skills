#include <stddef.h>
#include <stdint.h>

int32_t __cdecl compress_buffer(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    size_t* bytes_written);
