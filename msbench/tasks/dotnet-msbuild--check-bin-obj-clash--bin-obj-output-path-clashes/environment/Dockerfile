FROM mcr.microsoft.com/dotnet/sdk:9.0

# Install evaluation utilities
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 python3-pip jq bc git curl procps \
    && rm -rf /var/lib/apt/lists/*

# Create testbed directory
RUN mkdir -p /testbed /output /app

# Copy test project files into testbed
COPY test_files/ /testbed/


# Copy evaluation helpers
COPY eval_helpers/ /app/

WORKDIR /testbed
