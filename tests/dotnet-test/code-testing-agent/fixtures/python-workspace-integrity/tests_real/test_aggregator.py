from core.aggregator import mean, p95


def test_mean():
    assert mean([2, 4, 6]) == 4


def test_p95():
    assert p95([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) == 10
