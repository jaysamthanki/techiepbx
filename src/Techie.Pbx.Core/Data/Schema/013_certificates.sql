-- TLS certificates (piece 23). The database owns certificates like it owns everything else
-- (D4): the PEMs live here, and both consumers — Kestrel for the admin UI on 443 and the
-- generated pjsip TLS transport for SIP — are fed from this table rather than from files an
-- operator has to keep in step (D97).
--
-- KeyPem is a private key, so this table is as sensitive as Extensions.Secret: never logged,
-- never rendered into a page, and only ever written to disk as the combined PEM Asterisk reads
-- (0640, group asterisk, D18/D101).
--
-- ExpiresUtc is the leaf certificate's notAfter as text ("u" format), so a row can be sorted and
-- compared without parsing a PEM. LastError is the last failed ACME order's message, kept so the
-- daily renewal service (D100) has somewhere to say what went wrong.
CREATE TABLE Certificates (
    CertificateID  INTEGER PRIMARY KEY,
    Name           TEXT    NOT NULL UNIQUE,
    Hostnames      TEXT    NOT NULL,
    CertificatePem TEXT    NOT NULL DEFAULT '',
    ChainPem       TEXT    NOT NULL DEFAULT '',
    KeyPem         TEXT    NOT NULL DEFAULT '',
    ExpiresUtc     TEXT    NOT NULL DEFAULT '',
    LastError      TEXT    NULL,
    Enabled        INTEGER NOT NULL DEFAULT 1
);
