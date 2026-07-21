# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS openxml-validator
WORKDIR /src
COPY tools/OpenXmlValidator/OpenXmlValidator.csproj ./
RUN dotnet restore OpenXmlValidator.csproj
COPY tools/OpenXmlValidator/Program.cs ./
RUN dotnet publish OpenXmlValidator.csproj -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -o /out

FROM python:3.12-slim-bookworm AS runtime
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    WORDTOOLKIT_BIND_HOST=0.0.0.0 \
    WORDTOOLKIT_PORT=8787 \
    WORDTOOLKIT_STORAGE_ROOT=/data/sessions

RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates \
      fonts-dejavu-core \
      fonts-liberation2 \
      libreoffice-writer \
      poppler-utils \
      tini \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --uid 10001 --shell /usr/sbin/nologin wordtoolkit \
    && mkdir -p /app /data/sessions \
    && chown -R wordtoolkit:wordtoolkit /app /data

WORKDIR /app
COPY pyproject.toml README.md LICENSE ./
COPY src ./src
RUN pip install --no-cache-dir .
COPY --from=openxml-validator /out/wordtoolkit-openxml-validator /usr/local/bin/wordtoolkit-openxml-validator

USER 10001:10001
EXPOSE 8787
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8787/health', timeout=3)"
ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["wordtoolkit"]

