# syntax=docker/dockerfile:1

# Stage 1: Build and publish the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Clone the repository directly into the container's build folder
RUN git clone https://github.com/kristoffer-tvera/super-recruiter.git .

# 2. Restore dependencies using the cloned files
RUN dotnet restore "SuperRecruiter.Worker/SuperRecruiter.Worker.csproj"

# 3. Change directory to the worker project and publish
WORKDIR /src/SuperRecruiter.Worker
RUN dotnet publish "SuperRecruiter.Worker.csproj" -c Release -o /app /p:UseAppHost=false

# Stage 2: Final lightweight runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Copy the compiled binaries from the build stage
COPY --from=build /app .

ENV DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "SuperRecruiter.Worker.dll"]