-- An AI voice agent (F11, D199): a number that answers a caller and hands the call's audio to the
-- media service over Asterisk's AudioSocket, where a model listens and a voice replies. One row is
-- one agent with its own system prompt, so it knows who it is and what it may do.
--
-- Number is dialled like an extension and shares the internal context with everything else, so it
-- has to be a number nothing else has claimed (D57); it is also what a destination points at.
--
-- Greeting is said the instant the call answers, before the model is asked anything; blank means
-- the agent waits for the caller to speak first. VoiceId and Model are blank for the site defaults
-- in Settings (Ai.ElevenLabsVoiceId, Ai.OpenRouterModel, D198). TransferTo is the extension the call
-- continues to when the agent hands off; blank means the call ends with the agent.
--
-- No key, secret or provider credential lives here: those are settings (D198).
CREATE TABLE IF NOT EXISTS AiAgents (
    AiAgentID  INTEGER PRIMARY KEY,
    Number     TEXT    NOT NULL UNIQUE,
    Name       TEXT    NOT NULL,
    Prompt     TEXT    NOT NULL DEFAULT '',
    Greeting   TEXT    NOT NULL DEFAULT '',
    VoiceId    TEXT    NOT NULL DEFAULT '',
    Model      TEXT    NOT NULL DEFAULT '',
    TransferTo TEXT    NOT NULL DEFAULT '',
    Enabled    INTEGER NOT NULL DEFAULT 1
);
