# Lab environment

## Azure lab VM

| | |
|---|---|
| Size | Standard B2pls v2 (2 vCPU, 4 GB RAM, **ARM64**) |
| OS | Debian |
| Private IP | 10.8.20.8 |
| Asterisk | 22.11.0, built from source on aarch64 |
| Network | Azure NSG: no inbound SIP/RTP opened. Reached from the private network. |

Credentials are on the VM in `/root/tnpbx-spike-credentials.txt` (root only). Don't copy
them into the repo or docs.

## How it was built

[`src/Techie.Pbx.Core/scripts/build-asterisk-vm.sh`](../src/Techie.Pbx.Core/scripts/build-asterisk-vm.sh),
run as root. It:

1. Installs build dependencies.
2. Creates the `asterisk` system user.
3. Downloads the current Asterisk LTS tarball, checks its sha256, builds with bundled pjproject.
   `BUILD_NATIVE` is off so the binary isn't tied to one CPU model. Build jobs are capped at
   about 1 per GB of RAM.
4. Writes a minimal hand-made config: two extensions (1001, 1002) with random passwords, an
   echo test, AMI on 127.0.0.1, and a separate `security.log`.
5. Detects NAT (Azure metadata, then a public IP lookup) and adds NAT settings to the SIP
   transport if the public and private IPs differ. Override with `PUBLIC_IP=x.x.x.x`.
6. Installs a systemd unit running Asterisk as `asterisk`.

Re-running it skips the build (unless `--rebuild`), backs up `/etc/asterisk`, and writes new
config with new passwords.

Note: the lab VM was built with an earlier copy of the script that did not have NAT detection.
It's reached over its private IP, so NAT settings aren't needed there.

**The lab script's config uses `600` for the echo test. The app's generated dialplan uses `*43`.**

## Verified so far

- [x] Asterisk builds and runs on ARM64 Debian
- [x] Softphone registers, echo test works (audio both ways)
- [ ] Extension to extension call (1001 → 1002)

## Useful commands on the VM

```bash
sudo asterisk -rvvv                          # console
sudo asterisk -rx 'pjsip show endpoints'
sudo asterisk -rx 'pjsip show contacts'      # who is registered
sudo asterisk -rx 'dialplan show internal'
sudo tail -f /var/log/asterisk/security.log  # failed auth attempts
sudo journalctl -u asterisk -f
```

## Reaching AMI from a dev machine

AMI listens on 127.0.0.1 only. Tunnel it:

```bash
ssh -L 5038:127.0.0.1:5038 <user>@10.8.20.8
```

Then connect to `127.0.0.1:5038` locally with the `tnpbx` AMI user.

## If the VM ever needs to be reachable from the internet

- Open UDP 5060 and 10000–20000 in the NSG **only to specific source IPs**. Open 5060 gets
  scanned within minutes.
- Re-run the (current) script so NAT settings are written and passwords are regenerated.
