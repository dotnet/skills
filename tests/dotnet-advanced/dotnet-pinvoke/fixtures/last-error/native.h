#include <wchar.h>

// Windows-only API. Returns zero on failure and sets the calling thread's
// last-error value. wchar_t is UTF-16 for this ABI.
int open_device_w(const wchar_t* path);
