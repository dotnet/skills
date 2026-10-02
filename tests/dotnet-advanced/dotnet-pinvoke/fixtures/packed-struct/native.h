#include <stdbool.h>
#include <stdint.h>

#pragma pack(push, 1)
typedef struct packet_header {
    uint8_t kind;
    uint32_t length;
    uint16_t flags;
    bool compressed;
} packet_header;
#pragma pack(pop)
