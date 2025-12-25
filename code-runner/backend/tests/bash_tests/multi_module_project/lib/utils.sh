#!/bin/bash
# 工具函数库

log_info() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [INFO] $*"
}

log_error() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [ERROR] $*" >&2
}

check_script() {
    local script="$1"
    if [ ! -f "$script" ]; then
        log_error "脚本不存在: $script"
        return 1
    fi
    return 0
}