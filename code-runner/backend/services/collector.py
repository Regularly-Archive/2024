from pathlib import Path
from typing import Dict, List, Optional
import fnmatch
from models import Artifact
import mimetypes
from pydantic import BaseModel
import shutil
import os



class FileSnapshot(BaseModel):
    size: int
    mtime: float


class ArtifactCollector:

    def __init__(
            self, 
            project_id: str, 
            project_dir: str, 
            execution_id: str, 
            max_size: int = 30 * 1024 * 1024,  
            allowed_patterns: Optional[List[str]] = None, 
            ignored_dirs: Optional[List[str]] = None,
            static_root: Optional[Path] = None,
        ):
        self.project_id = project_id
        self.project_dir = Path(project_dir).resolve()
        self.max_size = max_size
        self.allowed_patterns = allowed_patterns or []
        self.ignored_dirs = ignored_dirs or []
        self._before: Dict[str, FileSnapshot] = {}

        base_static_root = static_root or Path('./static')
        self._static_root = base_static_root / "projects" / project_id / "executions" / execution_id / "artifacts"
        self._static_root.mkdir(parents=True, exist_ok=True)

    def _is_ignored_dir(self, path: Path) -> bool:
        return any(part in self.ignored_dirs for part in path.parts)

    def _is_allowed_file(self, rel_path: str) -> bool:
        if not self.allowed_patterns:
            return True

        return any(fnmatch.fnmatch(rel_path, p) for p in self.allowed_patterns)

    def _snapshot(self) -> Dict[str, FileSnapshot]:
        snapshot: Dict[str, FileSnapshot] = {}

        if not self.project_dir.exists():
            return snapshot

        for path in self._iter_project_files():
            try:
                if not path.is_file():
                    continue

                if self._is_ignored_dir(path.relative_to(self.project_dir)):
                    continue
                
                rel = str(path.relative_to(self.project_dir))

                stat = path.stat()
                snapshot[rel] = FileSnapshot(
                    size=stat.st_size,
                    mtime=stat.st_mtime
                )
            except (OSError, FileNotFoundError, PermissionError):
                continue

        return snapshot

    def _is_snapshot_changed(self, before: Optional[FileSnapshot], after: FileSnapshot) -> bool:
        if before is None:
            return True
        
        return before.size != after.size or before.mtime != after.mtime
    
    def _collect_artifact(self, rel_path: str, snap: FileSnapshot) -> Optional[Artifact]:
        if not self._is_allowed_file(rel_path):
            return None

        if snap.size > self.max_size:
            return None

        full_path = self.project_dir / rel_path

        if not full_path.exists():
            return None

        mime, _ = mimetypes.guess_type(full_path.name)

        dest_path = self._static_root / rel_path
        dest_path.parent.mkdir(parents=True, exist_ok=True)

        try:
            shutil.copy2(full_path, dest_path)
        except (OSError, PermissionError):
            return None

        return Artifact(
            name=full_path.name,
            path=rel_path,
            size=snap.size,
            mime=mime,
        )
    
    def _iter_project_files(self):
        for root, dirs, files in os.walk(self.project_dir):
            dirs[:] = [
                d for d in dirs
                if d not in self.ignored_dirs and not d.startswith(".")
            ]

            for name in files:
                if name.startswith("."):
                    continue

                yield Path(root) / name

    def snapshot_before(self) -> None:
        self._before = self._snapshot()

    def collect_after(self, ) -> List[Artifact]:
        after = self._snapshot()
        artifacts: List[Artifact] = []

        for rel_path, snap in after.items():
            before = self._before.get(rel_path)

            if not self._is_snapshot_changed(before, snap):
                continue

            artifact = self._collect_artifact(rel_path, snap)
            if artifact:
                artifacts.append(artifact)
            
        return sorted(artifacts, key=lambda a: a.path)
        
