FROM node:18

RUN useradd --create-home --no-log-init --shell /bin/bash sandbox \
  && adduser sandbox sudo 

USER sandbox
WORKDIR /home/sandbox

RUN npm config set registry https://registry.npmmirror.com \
  && npm init -y \
  && npm install mocha \
  && npm install chai \
  && npm install typescript

ENV PATH="/home/sandbox/node_modules/typescript/bin/:${PATH}"