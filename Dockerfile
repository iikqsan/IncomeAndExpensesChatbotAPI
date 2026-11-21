# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a project file changes.
COPY ExpenseBot.sln ./
COPY src/ExpenseBot.Api/ExpenseBot.Api.csproj src/ExpenseBot.Api/
COPY src/ExpenseBot.Application/ExpenseBot.Application.csproj src/ExpenseBot.Application/
COPY src/ExpenseBot.Infrastructure/ExpenseBot.Infrastructure.csproj src/ExpenseBot.Infrastructure/
RUN dotnet restore src/ExpenseBot.Api/ExpenseBot.Api.csproj

COPY src/ src/
RUN dotnet publish src/ExpenseBot.Api/ExpenseBot.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Summaries need the Asia/Bangkok timezone; make sure tzdata is present.
USER root
RUN if [ ! -e /usr/share/zoneinfo/Asia/Bangkok ]; then \
        apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*; \
    fi

COPY --from=build /app/publish .

# One JSON object per log line, so platforms can search fields such as EventId 1001 (unparsable messages).
# Override with Logging__Console__FormatterName=simple for human-readable output.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Logging__Console__FormatterName=json \
    Logging__Console__FormatterOptions__UseUtcTimestamp=true \
    Logging__Console__FormatterOptions__TimestampFormat=yyyy-MM-ddTHH:mm:ss.fffZ
EXPOSE 8080

# Non-root user that ships with the official .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseBot.Api.dll"]
