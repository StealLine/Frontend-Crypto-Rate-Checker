CREATE TABLE IF NOT EXISTS users (
    id          SERIAL PRIMARY KEY,
    username    VARCHAR(50)  UNIQUE NOT NULL,
    email       VARCHAR(255) UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
 
CREATE TABLE IF NOT EXISTS sessions (
    id          SERIAL PRIMARY KEY,
    user_id     INT          NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token       TEXT         UNIQUE NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    expires_at  TIMESTAMPTZ  NOT NULL
);
 
CREATE INDEX IF NOT EXISTS idx_sessions_token    ON sessions(token);
CREATE INDEX IF NOT EXISTS idx_sessions_expires  ON sessions(expires_at);
 