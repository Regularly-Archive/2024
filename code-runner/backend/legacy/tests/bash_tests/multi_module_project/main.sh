#!/bin/bash
echo "=== 多模块bash项目启动 ==="

# 引入配置
if [ -f config.sh ]; then
    source ./config.sh
fi

# 引入函数库
if [ -f lib/utils.sh ]; then
    source ./lib/utils.sh
fi

log_info "项目开始执行"
log_info "项目: $PROJECT_NAME v$VERSION"

# 执行子模块
if [ -d modules ]; then
    for module in modules/*.sh; do
        if [ -f "$module" ]; then
            log_info "执行模块: $module"
            bash "$module"
        fi
    done
fi

log_info "项目执行完成"