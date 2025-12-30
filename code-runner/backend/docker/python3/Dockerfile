FROM python:3.9.18

RUN mkdir -p /etc/pip \
 && printf "[global]\nindex-url = https://mirrors.aliyun.com/pypi/simple/\ntrusted-host = mirrors.aliyun.com\n" > /etc/pip.conf

RUN useradd --create-home --no-log-init --shell /bin/bash sandbox \
  && adduser sandbox sudo

USER sandbox

WORKDIR /home/sandbox