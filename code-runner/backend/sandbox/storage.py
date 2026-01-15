"""
SQLite storage for sandbox state persistence.

Provides reliable storage for sandbox metadata across service restarts.
"""
import os
import json
import sqlite3
from datetime import datetime
from typing import Optional, Dict, List, Any
from contextlib import contextmanager
from dataclasses import dataclass, asdict

from sandbox.models import SandboxStatus
from sandbox.docker_service import SandboxDockerClient
from services.logger import get_logger

logger = get_logger(__name__)


@dataclass
class SandboxRecord:
    """Internal representation of a sandbox record."""
    sandbox_id: str
    template_id: str
    container_id: str
    image_name: str
    workdir: str
    user: str
    status: str
    created_at: str
    expires_at: str
    file_hashes: str  # JSON string

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "SandboxRecord":
        return cls(**data)


class SandboxStorage:
    """
    SQLite-based storage for sandbox state.

    Features:
    - Persistent storage across restarts
    - Atomic operations
    - Thread-safe with connection pooling
    """

    def __init__(self, db_path: str = None):
        if db_path is None:
            # Store in user's cache directory
            cache_dir = os.path.join(os.path.expanduser("~"), ".cache", "code-runner")
            os.makedirs(cache_dir, exist_ok=True)
            db_path = os.path.join(cache_dir, "sandbox.db")

        self.db_path = db_path
        self._init_db()
        logger.info(f"Sandbox storage initialized at {db_path}")

    def _init_db(self):
        """Initialize the database schema."""
        with self._get_connection() as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS sandboxes (
                    sandbox_id TEXT PRIMARY KEY,
                    template_id TEXT NOT NULL,
                    container_id TEXT NOT NULL,
                    image_name TEXT NOT NULL,
                    workdir TEXT NOT NULL,
                    user TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'running',
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    file_hashes TEXT DEFAULT '{}',
                    created_at_db TEXT DEFAULT (datetime('now'))
                )
            """)
            conn.execute("""
                CREATE INDEX IF NOT EXISTS idx_sandboxes_status
                ON sandboxes(status)
            """)
            conn.execute("""
                CREATE INDEX IF NOT EXISTS idx_sandboxes_expires
                ON sandboxes(expires_at)
            """)
            conn.commit()

    @contextmanager
    def _get_connection(self):
        """Get a database connection."""
        conn = sqlite3.connect(self.db_path, timeout=30.0)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()

    def save_sandbox(self, record: SandboxRecord) -> bool:
        """Save or update a sandbox record."""
        try:
            with self._get_connection() as conn:
                conn.execute("""
                    INSERT OR REPLACE INTO sandboxes (
                        sandbox_id, template_id, container_id, image_name,
                        workdir, user, status, created_at, expires_at, file_hashes
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    record.sandbox_id,
                    record.template_id,
                    record.container_id,
                    record.image_name,
                    record.workdir,
                    record.user,
                    record.status,
                    record.created_at,
                    record.expires_at,
                    record.file_hashes,
                ))
                conn.commit()
            return True
        except sqlite3.Error as e:
            logger.error(f"Failed to save sandbox {record.sandbox_id}: {e}")
            return False

    def get_sandbox(self, sandbox_id: str) -> Optional[SandboxRecord]:
        """Get a sandbox by ID."""
        with self._get_connection() as conn:
            row = conn.execute(
                "SELECT * FROM sandboxes WHERE sandbox_id = ?",
                (sandbox_id,)
            ).fetchone()

            if row:
                return SandboxRecord(**dict(row))
            return None

    def list_sandboxes(
        self,
        status: Optional[str] = None,
        include_expired: bool = False
    ) -> List[SandboxRecord]:
        """List all sandboxes, optionally filtered by status."""
        query = "SELECT * FROM sandboxes"
        params = []

        if status:
            query += " WHERE status = ?"
            params.append(status)
        elif not include_expired:
            query += " WHERE status != 'terminated' AND status != 'error'"

        query += " ORDER BY created_at_db DESC"

        with self._get_connection() as conn:
            rows = conn.execute(query, params).fetchall()
            return [SandboxRecord(**dict(row)) for row in rows]

    def update_status(self, sandbox_id: str, status: str) -> bool:
        """Update sandbox status."""
        try:
            with self._get_connection() as conn:
                conn.execute(
                    "UPDATE sandboxes SET status = ? WHERE sandbox_id = ?",
                    (status, sandbox_id)
                )
                conn.commit()
            return True
        except sqlite3.Error as e:
            logger.error(f"Failed to update status for {sandbox_id}: {e}")
            return False

    def update_file_hashes(self, sandbox_id: str, hashes: Dict[str, str]) -> bool:
        """Update file hashes for a sandbox."""
        try:
            with self._get_connection() as conn:
                conn.execute(
                    "UPDATE sandboxes SET file_hashes = ? WHERE sandbox_id = ?",
                    (json.dumps(hashes), sandbox_id)
                )
                conn.commit()
            return True
        except sqlite3.Error as e:
            logger.error(f"Failed to update hashes for {sandbox_id}: {e}")
            return False

    def delete_sandbox(self, sandbox_id: str) -> bool:
        """Delete a sandbox record."""
        try:
            with self._get_connection() as conn:
                conn.execute(
                    "DELETE FROM sandboxes WHERE sandbox_id = ?",
                    (sandbox_id,)
                )
                conn.commit()
            return True
        except sqlite3.Error as e:
            logger.error(f"Failed to delete sandbox {sandbox_id}: {e}")
            return False

    def mark_expired(self) -> int:
        """Mark all expired sandboxes as terminated. Returns count."""
        now = datetime.now().isoformat()
        try:
            with self._get_connection() as conn:
                cursor = conn.execute(
                    "UPDATE sandboxes SET status = 'terminated' "
                    "WHERE status != 'terminated' AND expires_at < ?",
                    (now,)
                )
                conn.commit()
            return cursor.rowcount
        except sqlite3.Error as e:
            logger.error(f"Failed to mark expired sandboxes: {e}")
            return 0

    def cleanup_old_records(self, days: int = 7) -> int:
        """Delete terminated records older than N days. Returns count."""
        try:
            with self._get_connection() as conn:
                cursor = conn.execute(
                    "DELETE FROM sandboxes "
                    "WHERE status IN ('terminated', 'error') "
                    "AND created_at_db < datetime('now', ?)",
                    (f"-{days} days",)
                )
                conn.commit()
            return cursor.rowcount
        except sqlite3.Error as e:
            logger.error(f"Failed to cleanup old records: {e}")
            return 0

    def get_stats(self) -> Dict[str, int]:
        """Get storage statistics."""
        with self._get_connection() as conn:
            total = conn.execute("SELECT COUNT(*) FROM sandboxes").fetchone()[0]
            running = conn.execute(
                "SELECT COUNT(*) FROM sandboxes WHERE status = 'running'"
            ).fetchone()[0]
            terminated = conn.execute(
                "SELECT COUNT(*) FROM sandboxes WHERE status = 'terminated'"
            ).fetchone()[0]
            expired = conn.execute(
                "SELECT COUNT(*) FROM sandboxes WHERE expires_at < ? AND status != 'terminated'",
                (datetime.now().isoformat(),)
            ).fetchone()[0]

        return {
            "total": total,
            "running": running,
            "terminated": terminated,
            "expired": expired
        }


class SandboxRepository:
    """
    Repository pattern for sandbox operations.

    Combines storage with container management for recovery and cleanup.
    """

    def __init__(self, storage: SandboxStorage = None):
        self.storage = storage or SandboxStorage()
        self.docker = SandboxDockerClient()

    def save(self, instance: "SandboxInstance") -> bool:
        """Save a sandbox instance."""
        record = SandboxRecord(
            sandbox_id=instance.sandbox_id,
            template_id=instance.template_id,
            container_id=instance.container_id,
            image_name=instance.image_name,
            workdir=instance.workdir,
            user=instance.user,
            status=instance.status.value,
            created_at=instance.created_at.isoformat(),
            expires_at=instance.expires_at.isoformat(),
            file_hashes=json.dumps(instance.file_hashes)
        )
        return self.storage.save_sandbox(record)

    def load(self, sandbox_id: str) -> Optional["SandboxInstance"]:
        """Load a sandbox instance from storage."""
        from dataclasses import dataclass
        from datetime import datetime

        record = self.storage.get_sandbox(sandbox_id)
        if not record:
            return None

        # Verify container still exists
        container = self.docker.get_container_by_id(record.container_id)
        if not container:
            # Container is gone, mark as terminated
            self.storage.update_status(sandbox_id, "terminated")
            return None

        @dataclass
        class LoadedInstance:
            sandbox_id: str
            template_id: str
            container_id: str
            image_name: str
            workdir: str
            user: str
            status: SandboxStatus
            created_at: datetime
            expires_at: datetime
            file_hashes: Dict[str, str]

            @property
            def is_expired(self):
                return datetime.now() > self.expires_at

        return LoadedInstance(
            sandbox_id=record.sandbox_id,
            template_id=record.template_id,
            container_id=record.container_id,
            image_name=record.image_name,
            workdir=record.workdir,
            user=record.user,
            status=SandboxStatus(record.status),
            created_at=datetime.fromisoformat(record.created_at),
            expires_at=datetime.fromisoformat(record.expires_at),
            file_hashes=json.loads(record.file_hashes) if record.file_hashes else {}
        )

    def list_all(self) -> List["SandboxInstance"]:
        """Load all running sandboxes from storage."""
        records = self.storage.list_sandboxes(include_expired=False)
        instances = []

        for record in records:
            instance = self.load(record.sandbox_id)
            if instance:
                instances.append(instance)

        return instances

    def recover_orphaned(self) -> Dict[str, int]:
        """
        Recover sandboxes that were running before service restart.

        Returns dict with 'recovered' and 'terminated' counts.
        """
        result = {"recovered": 0, "terminated": 0}

        # Get all sandboxes that were running
        records = self.storage.list_sandboxes(status="running")

        for record in records:
            container = self.docker.get_container_by_id(record.container_id)
            if container and container.status == "running":
                # Still running, mark as recovered
                logger.info(
                    f"Recovered sandbox {record.sandbox_id} "
                    f"with container {record.container_id[:12]}"
                )
                result["recovered"] += 1
            else:
                # Container is gone, mark as terminated
                self.storage.update_status(record.sandbox_id, "terminated")
                logger.info(
                    f"Marked sandbox {record.sandbox_id} as terminated "
                    "(container not found)"
                )
                result["terminated"] += 1

        return result

    def cleanup_expired(self) -> int:
        """Mark and clean up expired sandboxes."""
        count = self.storage.mark_expired()
        if count > 0:
            logger.info(f"Marked {count} expired sandboxes as terminated")
        return count

    def destroy(self, sandbox_id: str) -> bool:
        """Delete sandbox record (after container is already removed)."""
        return self.storage.delete_sandbox(sandbox_id)
