typedef void (__cdecl *log_callback)(int level, const char* message);

void __cdecl set_log_callback(log_callback callback);
