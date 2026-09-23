FROM python:3.12-slim
WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY analytics/ ./analytics/
RUN pip install --no-cache-dir uv && uv sync --frozen --no-dev
ENV PATH="/app/.venv/bin:$PATH"
CMD ["uvicorn", "analytics.api.main:app", "--host", "0.0.0.0", "--port", "8000"]
