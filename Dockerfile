# syntax=docker/dockerfile:1

# Stage 1: Build and publish the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SuperRecruiter.Shared/SuperRecruiter.Shared.csproj SuperRecruiter.Shared/
COPY SuperRecruiter.Worker/SuperRecruiter.Worker.csproj SuperRecruiter.Worker/
RUN dotnet restore "SuperRecruiter.Worker/SuperRecruiter.Worker.csproj"
COPY SuperRecruiter.Shared/ SuperRecruiter.Shared/
COPY SuperRecruiter.Worker/ SuperRecruiter.Worker/
WORKDIR /src/SuperRecruiter.Worker
RUN dotnet publish "SuperRecruiter.Worker.csproj" -c Release -o /app --no-restore /p:UseAppHost=false

# Stage 2: Final lightweight runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Copy the compiled binaries from the build stage
COPY --from=build /app .

ENV DOTNET_RUNNING_IN_CONTAINER=true
USER $APP_UID
ENTRYPOINT ["dotnet", "SuperRecruiter.Worker.dll"]