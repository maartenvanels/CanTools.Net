# Interop tier: SdoClient vs a real lely-core SDO server (loopback over vcan)

This document describes an **optional, Linux-only** test tier that exercises
CanTools's `SdoClient` against a real CANopen implementation —
[lely-core](https://gitlab.com/lely_industries/lely-core) (Apache 2.0) — over a
virtual CAN interface (`vcan0`). It exists to catch protocol-level drift between
our SDO client and a widely used reference stack, beyond what the in-repo
fixture-based unit tests can prove.

**This tier is not part of the default `dotnet test` gate.** It requires:

- Linux (SocketCAN's `vcan` driver is not available on Windows or macOS),
- root/`sudo` access to create a `vcan0` network interface,
- a native build of lely-core, and
- the `CANTOOLS_INTEROP=1` environment variable set explicitly.

With none of that present — which is the default on every developer machine and
in the existing cross-platform CI job — the test
`SdoClientInteropTests.It_reads_the_device_type_from_a_lely_server` simply
**skips**. It never fails the build and never gates PRs. If you additionally set
`CANTOOLS_INTEROP=1` on a machine that hasn't done the rest of this setup, the
test still doesn't crash: `InteropChannel.OpenVcan0()` throws a `SkipException`
with a message pointing back at this guide, so the test reports as **skipped**
with clear instructions rather than failing with a confusing exception. See
"Why does it still skip after I set CANTOOLS_INTEROP=1?" below.

## 1. Build lely-core

lely-core ships the CANopen master/slave stack and the `coctl` command-line
tool, along with a Python `canopen`-compatible interface. Building it requires
CMake and a C toolchain.

```bash
git clone https://gitlab.com/lely_industries/lely-core.git
cd lely-core
mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
make -j"$(nproc)"
sudo make install
sudo ldconfig
```

Refer to lely-core's own `README.rst` for distro-specific dependencies
(`libbluetooth-dev`, `python3-dev`, etc. depending on which subsystems you
enable). lely-core is licensed Apache 2.0; nothing from its source is vendored
into this repository — this tier only talks to a lely process over CAN frames.

## 2. Create the vcan0 interface

`vcan` is SocketCAN's virtual CAN driver, part of the Linux kernel. It gives you
a loopback CAN bus with no physical hardware — ideal for this kind of interop
test.

```bash
sudo modprobe vcan
sudo ip link add dev vcan0 type vcan
sudo ip link set up vcan0
```

Verify it came up:

```bash
ip link show vcan0
```

## 3. Run a lely slave with a known object dictionary

Bring up a lely CANopen slave (server) on `vcan0`, configured with node id
`0x0A` (matches `InteropChannel.LelyNodeId` in the test) and an object
dictionary that includes at least the mandatory CiA 301 entries — in
particular `0x1000` (Device Type), which the interop test reads.

Using `coctl` from lely-core (or an equivalent lely slave sample/EDS-driven
slave):

```bash
# Example — adapt to the actual lely slave binary/EDS you use; consult
# lely-core's own examples/docs for the exact invocation and EDS format.
lely-slave --interface=vcan0 --node-id=10 --eds=/path/to/master.eds
```

The important properties for this test are:

- the slave is reachable on `vcan0`,
- its node id is `0x0A` (decimal 10),
- object `0x1000` sub `0x00` (Device Type, `UNSIGNED32`) is present and
  non-zero, per CiA 301.

Leave this process running for the duration of the test run.

## 4. Wire a real ICanChannel to vcan0

CanTools's core library (`src/CanTools`) intentionally ships **no** SocketCAN or
`vcan` adapter — it stays hardware/OS-dependent-library-free so it builds and
runs the same way on Windows, Linux, and macOS. `ICanChannel`
(`src/CanTools/Transport/ICanChannel.cs`) is the seam consumers use to bridge to
real hardware.

`tests/CanTools.Tests/CanOpen/InteropChannel.cs`'s `OpenVcan0()` is a
**placeholder**: as committed, it always throws `Xunit.SkipException` because no
such binding exists in this repository. To actually run this tier, replace its
body with a real channel bound to `vcan0`, for example:

- a small SocketCAN `PF_CAN`/`CAN_RAW` socket wrapper (P/Invoke against
  `socket(2)`/`bind(2)`/`read(2)`/`write(2)` with `sockaddr_can`), or
- the `CanKitCanChannel` bridge shown in `samples/CanTools.Sample` (see
  `docs/examples.md` for the sample walkthrough), configured against a CanKit
  SocketCAN backend on Linux.

Either way, the resulting type must implement `CanTools.Transport.ICanChannel`
(`SendAsync`/`ReceiveAsync` over `CanFrame`) and open the OS's `vcan0` device.
This is deliberately left as an integration point rather than shipped code,
since it pulls in Linux-only, socket-level dependencies that don't belong in
the cross-platform core.

## 5. Run the interop test

With the lely slave running and `InteropChannel.OpenVcan0()` wired to a real
`vcan0` channel:

```bash
export CANTOOLS_INTEROP=1
dotnet test tests/CanTools.Tests --filter SdoClientInteropTests
```

Expected result: `It_reads_the_device_type_from_a_lely_server` passes, having
performed an SDO upload of `0x1000` sub `0` against the lely slave and asserted
the returned Device Type is non-zero.

### Why does it still skip after I set CANTOOLS_INTEROP=1?

If you set `CANTOOLS_INTEROP=1` without also completing step 4 (replacing the
placeholder `InteropChannel.OpenVcan0()` with a real channel), the test still
reports as **skipped**, not failed — `OpenVcan0()` throws a `SkipException`
explaining exactly that: there's no bundled vcan adapter, and this guide
describes what to wire in. This is intentional: a bare `CANTOOLS_INTEROP=1` on
an unprepared machine (e.g. a misconfigured CI runner) should never look like a
flaky failure, it should read as "this environment isn't set up for the
interop tier."

## CI placement

This tier is **Linux-only, optional, and must never gate the cross-platform
build**. It does not run as part of the existing `test` job in
`.github/workflows/ci.yml` (which runs on both `ubuntu-latest` and
`windows-latest` and must stay hardware/OS-dependency-free). If wired into CI
at all, it belongs in its own job, for example:

```yaml
interop:
  runs-on: ubuntu-latest
  continue-on-error: true   # never blocks the required checks
  steps:
    - uses: actions/checkout@v7
    - uses: actions/setup-dotnet@v6
      with:
        dotnet-version: 10.0.x
    - name: Build lely-core and set up vcan0
      run: |
        # ... steps 1-2 above ...
    - name: Start lely slave
      run: |
        # ... step 3 above, backgrounded ...
    - name: Run interop tests
      env:
        CANTOOLS_INTEROP: "1"
      run: dotnet test tests/CanTools.Tests --filter SdoClientInteropTests
```

Such a job should be treated as informational (e.g. `continue-on-error: true`,
or simply not included in branch protection's required checks) so that an
environment or lely-side hiccup never blocks merges to a cross-platform
library.
