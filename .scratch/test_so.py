import ctypes
try:
    lib = ctypes.CDLL("/tmp/libe_sqlite3_local.so")
    lib.sqlite3_libversion.restype = ctypes.c_char_p
    print("LOAD_OK, sqlite version:", lib.sqlite3_libversion().decode())
except OSError as e:
    print("LOAD_FAIL:", e)
