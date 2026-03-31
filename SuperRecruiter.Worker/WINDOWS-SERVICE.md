# Running SuperRecruiter.Worker as a Windows Service

## 1. Publish

```powershell
cd SuperRecruiter.Worker
dotnet publish -c Release -o C:\Services\SuperRecruiter.Worker
```

## 2. Create the service with delayed start

```powershell
sc.exe create "SuperRecruiter.Worker" `
  binPath="C:\Services\SuperRecruiter.Worker\SuperRecruiter.Worker.exe" `
  start=delayed-auto `
  displayname="Super Recruiter Worker"
```

- `start=delayed-auto` means it starts automatically after all `auto` services have started — useful for waiting on networking/database availability.

## 3. (Optional) Set a description

```powershell
sc.exe description "SuperRecruiter.Worker" "Scrapes WoWProgress and Raider.IO for recruitment candidates."
```

## 4. Start the service

```powershell
sc.exe start "SuperRecruiter.Worker"
```

## Management

```powershell
# Stop
sc.exe stop "SuperRecruiter.Worker"

# Delete (after stopping)
sc.exe delete "SuperRecruiter.Worker"

# Check status
sc.exe query "SuperRecruiter.Worker"
```

## Configuration

The service reads `appsettings.json` from the publish directory (`C:\Services\SuperRecruiter.Worker\`). Copy or update `appsettings.json` there with your production settings (connection strings, API keys, bot token, etc.).
