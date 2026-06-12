# Unraid Deployment

## 1. Build the Image via SSH

SSH into your Unraid server and run:

```bash
docker build -t super-recruiter-worker https://github.com/kristoffer-tvera/super-recruiter.git#main
```

## 2. Add the Container in Unraid

1. Open the **Docker** tab in Unraid.
2. Click **Add Container**.
3. Set **Name** to `SuperRecruiter-Worker`.
4. Set **Repository** to `super-recruiter-worker`.
5. Click **Apply**.

## 3. Update After Pushing New Code

When you push new code to GitHub:

1. Re-run the build command over SSH.
2. In Unraid, open **Advanced View** (top right).
3. Click **Force Update** on the container.
