FROM rust:1.81-slim

RUN useradd --create-home --shell /bin/bash sandbox \
    && mkdir -p /home/sandbox \
    && chown -R sandbox:sandbox /home/sandbox

USER sandbox

WORKDIR /home/sandbox