"""Require actual desktop logoff, not locking/disconnecting. No session names logged."""
import ctypes as C
from ctypes import wintypes as W
import datetime as dt
import json
import threading
import time


class Session(C.Structure):
    _fields_ = [('id', W.DWORD), ('station', W.LPWSTR), ('state', W.DWORD)]


api = C.WinDLL('wtsapi32', use_last_error=True)
api.WTSEnumerateSessionsW.argtypes = [W.HANDLE, W.DWORD, W.DWORD, C.POINTER(C.POINTER(Session)), C.POINTER(W.DWORD)]
api.WTSEnumerateSessionsW.restype = W.BOOL
api.WTSQuerySessionInformationW.argtypes = [W.HANDLE, W.DWORD, C.c_int, C.POINTER(C.c_void_p), C.POINTER(W.DWORD)]
api.WTSQuerySessionInformationW.restype = W.BOOL
api.WTSFreeMemory.argtypes = [C.c_void_p]


def desktop_sessions():
    sessions = C.POINTER(Session)()
    count = W.DWORD()
    if not api.WTSEnumerateSessionsW(None, 0, 1, C.byref(sessions), C.byref(count)):
        raise RuntimeError('SessionEnumerationUnavailable')
    signed_in, unknown = 0, 0
    try:
        for i in range(count.value):
            if sessions[i].id == 0:
                continue
            value = C.c_void_p()
            size = W.DWORD()
            if not api.WTSQuerySessionInformationW(None, sessions[i].id, 5, C.byref(value), C.byref(size)):
                unknown += 1
                continue
            try:
                # Disconnected users still have a username and must count as signed in.
                if value.value and C.wstring_at(value):
                    signed_in += 1
            finally:
                api.WTSFreeMemory(value)
    finally:
        api.WTSFreeMemory(sessions)
    return signed_in, unknown


class LogoffMonitor:
    def __init__(self):
        self.started = dt.datetime.now(dt.timezone.utc).isoformat()
        self.interrupted = False
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.observe, daemon=True)
        self.thread.start()

    def observe(self):
        while not self.stop.is_set():
            try:
                if desktop_sessions() != (0, 0):
                    self.interrupted = True
            except Exception:
                self.interrupted = True
            self.stop.wait(1)

    def finish(self):
        self.stop.set()
        self.thread.join(timeout=3)
        try:
            clear = desktop_sessions() == (0, 0)
        except Exception:
            clear = False
        return {'desktopLogoffVerified': clear and not self.interrupted,
                'logoffObservedAtUtc': self.started,
                'desktopSessionObservedDuringRun': self.interrupted}


def wait_for_logoff(root, timeout=600):
    deadline = time.monotonic()+timeout
    while time.monotonic() < deadline:
        count, unknown = desktop_sessions()
        status = {'stage': 'WaitingForWindowsSignOut', 'signedInDesktopSessions': count,
                  'unqueryableSessions': unknown, 'updatedAtUtc': dt.datetime.now(dt.timezone.utc).isoformat()}
        (root/'logoff-profile-test.json').write_text(json.dumps(status), encoding='utf-8')
        if (count, unknown) == (0, 0):
            monitor = LogoffMonitor()
            status.update(stage='WindowsSignedOutProfileTestStarting', signedOutObservedAtUtc=monitor.started)
            (root/'logoff-profile-test.json').write_text(json.dumps(status), encoding='utf-8')
            return monitor
        time.sleep(5)
    raise RuntimeError('WindowsSignOutNotObserved')
