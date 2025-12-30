"""执行管道和阶段定义"""

import asyncio
import os
import time
import json
import re
from abc import ABC, abstractmethod
from typing import Dict, Any, List, Optional
from dataclasses import dataclass, field
from enum import Enum


class ExecutionStatus(Enum):
    READY = "ready"
    RUNNING = "running"
    SUCCESS = "success"
    FAILED = "failed"
    TIMEOUT = "timeout"


@dataclass
class ExecutionContext:
    """执行上下文"""
    request: Dict[str, Any]
    config: Dict[str, Any]
    temp_dir: str = ""
    container_id: str = ""
    output_file: str = ""
    status: ExecutionStatus = ExecutionStatus.READY
    start_time: float = field(default_factory=time.time)
    end_time: Optional[float] = None
    output: str = ""
    error: str = ""
    exit_code: int = 0
    metadata: Dict[str, Any] = field(default_factory=dict)

    def get_duration(self) -> float:
        end = self.end_time or time.time()
        return end - self.start_time

    def update_status(self, status: ExecutionStatus):
        self.status = status
        if status in [ExecutionStatus.SUCCESS, ExecutionStatus.FAILED, ExecutionStatus.TIMEOUT]:
            self.end_time = time.time()


class ExecutionStage(ABC):
    """执行阶段基类"""

    @abstractmethod
    async def execute(self, context: ExecutionContext) -> ExecutionContext:
        """执行阶段处理逻辑"""
        pass

    @abstractmethod
    def get_name(self) -> str:
        """获取阶段名称"""
        pass

    def validate_context(self, context: ExecutionContext) -> bool:
        """验证执行上下文是否满足要求"""
        return True


class ExecutionPipeline:
    """执行管道"""

    def __init__(self, stages: List[ExecutionStage]):
        self.stages = stages
        self.stage_outputs: Dict[str, Any] = {}

    async def execute(self, initial_context: ExecutionContext) -> ExecutionContext:
        """执行管道中的所有阶段"""
        context = initial_context

        for stage in self.stages:
            stage_name = stage.get_name()

            # 验证上下文
            if not stage.validate_context(context):
                raise ValueError(f"Context validation failed for stage: {stage_name}")

            # 执行阶段
            try:
                context = await stage.execute(context)

                # 保存阶段输出
                self.stage_outputs[stage_name] = {
                    "status": context.status.value,
                    "duration": context.get_duration(),
                    "metadata": context.metadata.copy()
                }

                # 如果执行失败，停止管道
                if context.status == ExecutionStatus.FAILED:
                    break

            except Exception as e:
                context.error = str(e)
                context.status = ExecutionStatus.FAILED
                self.stage_outputs[stage_name] = {
                    "status": "error",
                    "error": str(e)
                }
                break

        return context


class CompositeStage(ExecutionStage):
    """复合阶段，包含多个子阶段"""

    def __init__(self, stages: List[ExecutionStage], name: str = "composite"):
        self.stages = stages
        self.name = name

    def get_name(self) -> str:
        return self.name

    async def execute(self, context: ExecutionContext) -> ExecutionContext:
        """顺序执行所有子阶段"""
        for stage in self.stages:
            if context.status == ExecutionStatus.FAILED:
                break

            context = await stage.execute(context)

        return context