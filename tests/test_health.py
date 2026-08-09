"""
Tests run in CI with no database available.

That constraint is deliberate: it forces the app to start and report healthy
without Postgres, which is exactly the behaviour the container probes depend
on. If you find yourself wanting a database in CI to make a test pass, the
app has probably grown a startup dependency it should not have.
"""

from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)


def test_health_returns_ok():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_health_does_not_require_a_database():
    # Passing at all proves the point: DATABASE_URL is unset in CI.
    assert client.get("/health").status_code == 200


def test_ready_reports_unconfigured_database():
    response = client.get("/ready")
    assert response.status_code == 200
    assert response.json()["database"] in {"unconfigured", "unavailable"}


def test_items_fails_cleanly_without_a_database():
    # 503, not a stack trace.
    response = client.get("/items")
    assert response.status_code == 503


def test_create_item_rejects_empty_title():
    response = client.post("/items", json={"title": ""})
    assert response.status_code == 422
