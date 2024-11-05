FROM golang:latest

RUN useradd --create-home --no-log-init --shell /bin/bash sandbox \
  && adduser sandbox sudo 

USER sandbox

WORKDIR /home/sandbox