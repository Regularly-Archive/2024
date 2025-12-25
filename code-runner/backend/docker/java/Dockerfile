FROM openjdk:20-jdk-slim

RUN useradd --create-home --no-log-init --shell /bin/bash sandbox \
  && adduser sandbox sudo


USER root
RUN apt-get update && \
    apt-get install -y curl unzip wget ca-certificates && \
    curl -L https://dlcdn.apache.org/maven/maven-3/3.9.12/binaries/apache-maven-3.9.12-bin.tar.gz -o /tmp/maven.tar.gz && \
    tar -xzf /tmp/maven.tar.gz -C /opt && \
    ln -s /opt/apache-maven-3.9.12 /opt/maven && \
    ln -s /opt/maven/bin/mvn /usr/local/bin/mvn && \
    rm /tmp/maven.tar.gz && \
    mvn --version && \
    curl -Ls https://github.com/jbangdev/jbang/releases/download/v0.116.0/jbang-0.116.0.zip -o /tmp/jbang.zip && \
    unzip /tmp/jbang.zip -d /usr/local && \
    ln -s /usr/local/jbang*/bin/jbang /usr/local/bin/jbang && \
    rm /tmp/jbang.zip && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Set environment variables for Maven
ENV M2_HOME=/opt/maven
ENV PATH=$M2_HOME/bin:$PATH
ENV MAVEN_HOME=/opt/maven

USER sandbox
WORKDIR /home/sandbox