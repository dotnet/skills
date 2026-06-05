"""User service backed by an in-memory dictionary."""

from __future__ import annotations


class UserNotFoundError(Exception):
    pass


class UserService:
    def __init__(self) -> None:
        self._users: dict[int, str] = {}

    def add(self, user_id: int, name: str) -> None:
        if user_id in self._users:
            raise ValueError(f"User {user_id} already exists")
        if not name:
            raise ValueError("name is required")
        self._users[user_id] = name

    def get(self, user_id: int) -> str:
        if user_id not in self._users:
            raise UserNotFoundError(str(user_id))
        return self._users[user_id]

    def delete(self, user_id: int) -> None:
        self._users.pop(user_id, None)

    def count(self) -> int:
        return len(self._users)
