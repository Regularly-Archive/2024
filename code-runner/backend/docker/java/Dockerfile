FROM openjdk:20-jdk-slim

RUN useradd --create-home --no-log-init --shell /bin/bash sandbox \
  && adduser sandbox sudo 


USER root
RUN apt-get update && apt-get install -y curl && \
    curl -Ls https://github.com/jbangdev/jbang/releases/download/v0.116.0/jbang-0.116.0.zip -o /tmp/jbang.zip && \
    apt-get install -y unzip && \
    unzip /tmp/jbang.zip -d /usr/local && \
    ln -s /usr/local/jbang*/bin/jbang /usr/local/bin/jbang && \
    rm /tmp/jbang.zip
    
USER sandbox
WORKDIR /home/sandbox