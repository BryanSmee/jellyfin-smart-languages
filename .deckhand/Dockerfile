# .deckhand/Dockerfile: .NET 10 SDK and Jellyfin's sources at v12.4
ARG DECKHAND_BASE=deckhand-sandbox
FROM $DECKHAND_BASE
USER root

# .NET 10 SDK: Jellyfin 12.4 targets net10.0 (its global.json asks for SDK 10.0).
RUN apt-get update && apt-get install -y --no-install-recommends libicu72 \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Jellyfin's server and web client source, pinned to their v12.4 commits: read-only references.
# The build fails if a tag no longer points at its commit; bump the tag and commit together.
ARG JELLYFIN_TAG=v12.4
ARG JELLYFIN_COMMIT=83d0d0e7f76e23e2e085c8ff67d28b772d35967d
ARG JELLYFIN_WEB_COMMIT=0a580ce8df89282dfd120b6d5efb8e106da23bd5
RUN git clone --depth 1 --branch "$JELLYFIN_TAG" https://github.com/jellyfin/jellyfin.git /opt/jellyfin \
    && test "$(git -C /opt/jellyfin rev-parse HEAD)" = "$JELLYFIN_COMMIT" \
    && git clone --depth 1 --branch "$JELLYFIN_TAG" https://github.com/jellyfin/jellyfin-web.git /opt/jellyfin-web \
    && test "$(git -C /opt/jellyfin-web rev-parse HEAD)" = "$JELLYFIN_WEB_COMMIT" \
    && rm -rf /opt/jellyfin/.git /opt/jellyfin-web/.git \
    && chmod -R a+rX,a-w /opt/jellyfin /opt/jellyfin-web
RUN mkdir -p /home/node/.nuget && chown node:node /home/node/.nuget

USER node
