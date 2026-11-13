FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends libreoffice-writer fonts-liberation fonts-crosextra-carlito fonts-crosextra-caladea && rm -rf /var/lib/apt/lists/*
COPY convert /usr/local/bin/convert-word
ENTRYPOINT ["/bin/sh", "/usr/local/bin/convert-word"]
