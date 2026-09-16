# Quotely API — deterministic .NET 10 image for Render's free web-service tier.
#
# Multi-stage so the shipped image carries the runtime and the published output only: no SDK, no
# NuGet cache, no source. Nothing environment-specific is baked in — the connection string, the JWT
# key, the Razorpay credentials and the allowed CORS origins all arrive as environment variables at
# start-up, so the same image is the one that ran in TEST.

# ---- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The project file alone first, so a source-only change reuses the restore layer.
COPY Quotely.Api/Quotely.Api.csproj Quotely.Api/
RUN dotnet restore Quotely.Api/Quotely.Api.csproj

COPY Quotely.Api/ Quotely.Api/
RUN dotnet publish Quotely.Api/Quotely.Api.csproj -c Release -o /app --no-restore

# ---- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# QuestPDF renders through SkiaSharp, which needs fontconfig present to resolve a typeface.
# Without it PDF generation fails at the first request rather than at start-up.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Render supplies PORT and the app binds 0.0.0.0 to it; 8080 is the fallback the base image uses.
EXPOSE 8080

# The base image ships a non-root account. The API writes nothing to disk — PDFs are generated in
# memory and streamed — so it has no reason to run as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "Quotely.Api.dll"]
