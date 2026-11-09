# Runs inside a headless editor: opens this process to ptrace by the jmap dumper, reports its PID on the pid fifo,
# and blocks until the release fifo is written. The fifo directory comes from JMAP_FIFOS.
import ctypes, os
libc = ctypes.CDLL(None)
libc.prctl(0x59616d61, ctypes.c_ulong(-1), 0, 0, 0)  # PR_SET_PTRACER, PR_SET_PTRACER_ANY
fifos = os.environ["JMAP_FIFOS"]
with open(os.path.join(fifos, "pid"), "w") as f:
    f.write(str(os.getpid()))
open(os.path.join(fifos, "release")).read()
