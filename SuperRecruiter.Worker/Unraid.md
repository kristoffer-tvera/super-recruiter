# Unraid Deployment

This is the home-only WoWProgress collector. The production deployment workflow
builds and runs the separate `Server` worker on the droplet; do not run that mode
on Unraid.

## 1. Build the Image via SSH

Push the changes you want to deploy, then SSH into Unraid and build the worker
from the latest `master` commit:

```bash
docker build --pull -t super-recruiter-worker https://github.com/kristoffer-tvera/super-recruiter.git#master
```

This builds the checked-in source on `master`; unpushed local changes are not
included.

## 2. Add the Container in Unraid

1. Open the **Docker** tab in Unraid.
2. Click **Add Container**.
3. Set **Name** to `SuperRecruiter-Worker`.
4. Set **Repository** to `super-recruiter-worker`.
5. Click **Apply**.

Configure these environment variables for the home-only collector:

```text
WorkerMode=WowProgress
SuperRecruiterApi__BaseUrl=https://recruitment.amusedtodeath.eu/api/
SuperRecruiterApi__ApiKey=<API key>
FlareSolverrUrl=http://<local-solver-host>:8191/v1
PollingIntervalMinutes=30
WowProgress__JobPollingIntervalSeconds=10
```

The API key must match the API's `ApiKey`. `FlareSolverrUrl` must be reachable
from the collector container; `localhost` inside this container refers to the
collector itself, not a separate FlareSolverr container. Do not publish ports
for the collector or expose FlareSolverr publicly. Do not provide database,
Discord, Raider.IO, or WarcraftLogs credentials to this collector. Discord and
Raider.IO discovery run in `Server` mode on the droplet.

## 3. Update After Pushing New Code

When you push new code to GitHub:

1. Re-run the build command over SSH to build the latest `master` source.
2. In Unraid, open **Advanced View** (top right).
3. Click **Force Update** on the container.
