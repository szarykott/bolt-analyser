"""JSON forbids NaN/Infinity; non-finite floats become None at the core boundary."""

import math


def finite_or_none(value: float) -> float | None:
    value = float(value)
    return value if math.isfinite(value) else None


def matrix_finite_or_none(values) -> list[list[float | None]]:
    return [[finite_or_none(v) for v in row] for row in values]
