#!/bin/bash

# 定义基础镜像名称
BASE_IMAGE_NAME="code_runner"

# 遍历当前目录下的每个文件夹
for dir in */; do
    DIR_NAME="${dir%/}"

    # 构建 Docker 镜像
    echo "Building Docker image: ${BASE_IMAGE_NAME}/${DIR_NAME} from ./${dir}..."
    
    # 使用 docker build 命令构建镜像
    
    docker build -t "${BASE_IMAGE_NAME}/${DIR_NAME}" ./${DIR_NAME}

    # 检查构建是否成功
    if [ $? -eq 0 ]; then
        echo "Successfully built image ${BASE_IMAGE_NAME}/${DIR_NAME}"
    else
        echo "Failed to build image ${BASE_IMAGE_NAME}/${DIR_NAME}"
    fi
done