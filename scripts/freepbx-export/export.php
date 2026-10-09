#!/usr/bin/env php
<?php
/**
 * TNPBX FreePBX exporter.
 *
 * Run ON the FreePBX box (root, or any user that can read the asterisk MySQL database):
 *
 *     php export.php [-o tnpbx-migrate.tar.gz]
 *
 * Reads the `asterisk` database, /etc/asterisk/voicemail.conf, the voicemail spool and the
 * custom sounds directory, and writes a tarball whose manifest.json is the contract the
 * TNPBX importer consumes (docs/freepbx-import.md in the TNPBX repo, manifest v1).
 *
 * The manifest is a faithful copy of what FreePBX had: no translation of dial-pattern
 * grammar, no codec choices, no renaming. Every FreePBX-version difference is absorbed here,
 * because this runs on the box where the real data can be inspected.
 *
 * Tested against FreePBX 16 / Asterisk 16 (chan_sip + pjsip mixed) and FreePBX 17 /
 * Asterisk 22. Secrets are stored plaintext by FreePBX; nothing here decrypts anything.
 */

error_reporting(E_ALL);
ini_set('display_errors', '1');

$opt = getopt('o:h', ['output:', 'help']);
if (isset($opt['h']) || isset($opt['help'])) {
    echo "php export.php [-o tnpbx-migrate.tar.gz]\n";
    exit(0);
}
$output = $opt['o'] ?? $opt['output'] ?? 'tnpbx-migrate.tar.gz';

/**
 * Reads $amp_conf values out of /etc/freepbx.conf (or the path given), which is a PHP file
 * FreePBX itself loads. Parsed with a regex rather than include()d so this script never
 * executes FreePBX code, and so it can be run read-only on any box.
 */
function ampConf(string $file): array {
    $conf = [
        'AMPDBHOST' => 'localhost', 'AMPDBNAME' => 'asterisk',
        'AMPDBUSER' => 'root',     'AMPDBPASS' => '', 'AMPDBPORT' => '',
    ];
    if (!is_readable($file)) {
        fwrite(STDERR, "Cannot read $file; pass a readable FreePBX config.\n");
        exit(1);
    }
    $text = file_get_contents($file);
    foreach (array_keys($conf) as $key) {
        $pat = "/\\\$amp_conf\\[['\"]" . preg_quote($key, '/') . "['\"]\\]\\s*=\\s*['\"]([^'\"]*)['\"]/";
        if (preg_match($pat, $text, $m)) {
            $conf[$key] = $m[1];
        }
    }
    return $conf;
}

$amp = ampConf('/etc/freepbx.conf');
$dsn = 'mysql:host=' . $amp['AMPDBHOST'] . ($amp['AMPDBPORT'] ? ';port=' . $amp['AMPDBPORT'] : '') . ';dbname=' . $amp['AMPDBNAME'];
try {
    $db = new PDO($dsn, $amp['AMPDBUSER'], $amp['AMPDBPASS'], [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
    ]);
} catch (PDOException $e) {
    fwrite(STDERR, "Cannot connect to the FreePBX database: " . $e->getMessage() . "\n");
    exit(1);
}

/** Every query, so a missing table on an old box is a warning, not a dead export. */
function q(PDO $db, string $sql, array &$warnings): array {
    try {
        return $db->query($sql)->fetchAll();
    } catch (PDOException $e) {
        $warnings[] = 'SQL failed (' . $e->errorInfo[1] ?? '?' . '): ' . substr($sql, 0, 80);
        return [];
    }
}

$warnings = [];
$manifest = [
    'manifestVersion' => 1,
    'exported' => gmdate('c'),
    'source' => [
        'distribution' => 'FreePBX',
        'version' => trim((string)shell_exec("fwconsole --version 2>/dev/null | awk '{print \$NF}'") ?? ''),
        'asterisk' => trim((string)shell_exec("asterisk -rx 'core show version' 2>/dev/null | awk '{print \$2}'") ?? ''),
    ],
    'extensions' => [], 'trunks' => [], 'outboundRoutes' => [], 'inboundRoutes' => [],
    'sounds' => [], 'phones' => [], 'warnings' => [],
];

/**
 * Parsed /etc/asterisk/voicemail.conf: one entry per mailbox in the shape FreePBX writes,
 * `ext=pin,name,email,pager,options`. The PINs are only here, never in the database.
 */
function readVoicemailConf(string $file, array &$warnings): array {
    $boxes = [];
    if (!is_readable($file)) { $warnings[] = "Cannot read $file (voicemail PINs and emails unavailable)."; return $boxes; }
    $section = '';
    foreach (file($file, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES) ?: [] as $line) {
        $line = trim($line);
        if ($line === '' || $line[0] === ';') continue;
        if ($line[0] === '[') { $section = trim($line, '[]'); continue; }
        if (!preg_match('/^([^=]+)=(.*)$/', $line, $m)) continue;
        $fields = explode(',', $m[2], 5); // pin,name,email,pager,rest|options
        $rest = $fields[4] ?? '';
        $opts = [];
        foreach (explode('|', $rest) as $pair) {
            $kv = explode('=', $pair, 2);
            if (count($kv) === 2) $opts[trim($kv[0])] = trim($kv[1]);
        }
        $boxes[$m[1]] = [
            'context' => $section,
            'pin' => $fields[0] ?? '',
            'name' => $fields[1] ?? '',
            'email' => $fields[2] ?? '',
            'attach' => ($opts['attach'] ?? '') === 'yes',
        ];
    }
    return $boxes;
}
$vmBoxes = readVoicemailConf('/etc/asterisk/voicemail.conf', $warnings);

// Extensions: the users table is the spine; secrets come from sip/pjsip rows for the same id.
$sipSecrets = []; // id => [keyword => data]
foreach (q($db, 'SELECT id, keyword, data FROM sip', $warnings) as $row) $sipSecrets[$row['id']][$row['keyword']] = $row['data'];
$pjsip = [];      // id => [keyword => data]
foreach (q($db, 'SELECT id, keyword, data FROM pjsip', $warnings) as $row) $pjsip[$row['id']][$row['keyword']] = $row['data'];
$devices = [];
foreach (q($db, 'SELECT id, tech, user, description FROM devices', $warnings) as $row) $devices[$row['id']] = $row;

foreach (q($db, 'SELECT extension, password, name, outboundcid, sipname FROM users ORDER BY extension', $warnings) as $u) {
    $ext = $u['extension'];
    $dev = $devices[$ext] ?? null;
    $secret = $sipSecrets[$ext]['secret'] ?? $pjsip[$ext]['secret'] ?? '';
    if ($secret === '') $warnings[] = "Extension $ext has no SIP secret in sip/pjsip.";
    $box = $vmBoxes[$ext] ?? null;
    $manifest['extensions'][] = [
        'number' => $ext,
        'name' => $u['name'],
        'secret' => $secret,
        'outboundCallerId' => $u['outboundcid'],
        'tech' => $dev['tech'] ?? 'sip',
        'voicemailEnabled' => $box !== null,
        'voicemailPin' => $box['pin'] ?? '',
        'voicemailEmail' => $box['email'] ?? '',
        'voicemailAttach' => $box['attach'] ?? true,
        'voicemailContext' => $box['context'] ?? 'default',
        'voicemailMessages' => 0, // filled below from the spool
    ];
}

// Trunks: the trunks table plus the pjsip rows FreePBX keeps under the numeric trunkid, or
// the chan_sip rows under tr-peer-<id>/tr-user-<id>.
$trunkRows = q($db, 'SELECT trunkid, tech, channelid, name, outcid, keepcid, maxchans, dialoutprefix, usercontext, provider, disabled FROM trunks ORDER BY trunkid', $warnings);
$trunkNameById = [];
foreach ($trunkRows as $t) {
    $trunkNameById[$t['trunkid']] = $t['name'];
    $id = $t['trunkid'];
    $p = $pjsip[$id] ?? [];
    $peer = $sipSecrets['tr-peer-' . $id] ?? [];
    $user = $sipSecrets['tr-user-' . $id] ?? [];
    // pjsip trunk: server_uri sip:host or sip_server + sip_server_port; chan_sip: host:port
    $host = $p['sip_server'] ?? $peer['host'] ?? '';
    $port = (int)($p['sip_server_port'] ?? 5060);
    if ($host === '' && isset($p['server_uri']) && preg_match('#^sips?://([^:/]+)(?::(\d+))?#', $p['server_uri'], $m)) {
        $host = $m[1]; $port = (int)($m[2] ?? 5060);
    }
    $manifest['trunks'][] = [
        'name' => $t['name'],
        'tech' => $t['tech'],
        'channelId' => $t['channelid'],
        'serverHost' => $host,
        'serverPort' => $port,
        'username' => $p['username'] ?? $peer['username'] ?? $user['username'] ?? '',
        'authUsername' => $p['auth_username'] ?? '',
        'password' => $p['secret'] ?? $peer['secret'] ?? $user['secret'] ?? '',
        'register' => ($p['registration'] ?? '') === 'send',
        'matchAddresses' => $p['match'] ?? '',
        'callerIdNumber' => $t['outcid'],
        'fromUser' => $p['from_user'] ?? '',
        'fromDomain' => $p['from_domain'] ?? '',
        'codecs' => $p['codecs'] ?? '',
        'disabledInFreePBX' => $t['disabled'] === 'on',
    ];
    if (($manifest['trunks'][count($manifest['trunks']) - 1]['password'] ?? '') === '') {
        $warnings[] = 'Trunk ' . $t['name'] . ' has no recoverable secret.';
    }
}

// Outbound routes: FreePBX keeps one row per pattern and one row per trunk in the sequence.
$patterns = [];
foreach (q($db, 'SELECT route_id, match_pattern_prefix, match_pattern_pass, prepend_digits FROM outbound_route_patterns', $warnings) as $r) {
    $patterns[$r['route_id']][] = $r;
}
$routeTrunks = [];
foreach (q($db, 'SELECT route_id, trunk_id, seq FROM outbound_route_trunks ORDER BY route_id, seq', $warnings) as $r) {
    $routeTrunks[$r['route_id']][] = $trunkNameById[$r['trunk_id']] ?? null;
}
foreach (q($db, 'SELECT route_id, name, outcid, emergency_route, intracompany_route, mohclass, dest FROM outbound_routes ORDER BY route_id', $warnings) as $r) {
    foreach ($patterns[$r['route_id']] ?? [] as $p) {
        $manifest['outboundRoutes'][] = [
            'name' => $r['name'],
            'priority' => (int)$r['route_id'],
            'trunkName' => $routeTrunks[$r['route_id']][0] ?? null,
            'trunkSequence' => $routeTrunks[$r['route_id']] ?? [],
            'dialPattern' => $p['match_pattern_pass'],
            'prependDigits' => $p['match_pattern_prefix'] ?: $p['prepend_digits'],
            'callerId' => $r['outcid'],
            'emergency' => $r['emergency_route'] === 'yes',
        ];
    }
}

// Inbound routes: FreePBX's incoming table, destinations left raw for the importer to translate.
foreach (q($db, 'SELECT extension, cidnum, destination, description, mohclass FROM incoming', $warnings) as $r) {
    $manifest['inboundRoutes'][] = [
        'did' => $r['extension'],
        'callerIdMatch' => $r['cidnum'],
        'trunkName' => null, // FreePBX does not bind an inbound route to a trunk
        'description' => $r['description'],
        'mohclass' => $r['mohclass'],
        'catchAll' => $r['extension'] === '' && $r['cidnum'] === '',
        'destination' => $r['destination'],
    ];
}

// Sounds: the FreePBX recordings module stores custom audio under sounds/custom, and the
// announcement table names them. Carry the files byte-identical; conversion is TNPBX's job.
$recordings = [];
foreach (q($db, 'SELECT id, displayname, filename FROM recordings', $warnings) as $r) {
    $recordings[(int)$r['id']] = $r;
}
$announcementFiles = [];
foreach (q($db, 'SELECT announcement_id, description, recording_id FROM announcement', $warnings) as $a) {
    $rec = $recordings[(int)$a['recording_id']] ?? null;
    // The recordings table stores paths like "custom/Seaforth_Ln" while the file itself
    // lives under sounds/en/custom/Seaforth_Ln.wav, so match by basename stem.
    if ($rec) $announcementFiles[preg_replace('/\.(wav|gsm|ulaw|alaw|g722)$/i', '', basename($rec['filename']))] = $rec['displayname'];
}
$soundsRoot = '/var/lib/asterisk/sounds';
$staged = []; // tar path => absolute source path
foreach (['custom', 'en/custom'] as $sub) {
    foreach (glob($soundsRoot . '/' . $sub . '/*') ?: [] as $f) {
        // Audio only: the folder also catches editor droppings (Recording.sln) that no PBX plays.
        if (!is_file($f) || !preg_match('/\.(wav|gsm|ulaw|alaw|g722|mp3)$/i', $f)) continue;
        $staged['files/sounds/' . $sub . '/' . basename($f)] = $f;
        $manifest['sounds'][] = [
            'filename' => $sub . '/' . basename($f),
            'announcementName' => $announcementFiles[preg_replace('/\.(wav|gsm|ulaw|alaw|g722)$/i', '', basename($f))] ?? null,
        ];
    }
}

// Phones: the Polycom module's own tables. polycom_device_lines.id is polycom_devices.id
// (verified against the working module), and its deviceid column is the assigned extension.
$linesByDevice = [];
foreach (q($db, 'SELECT id, lineid, deviceid, externalid FROM polycom_device_lines', $warnings) as $l) {
    $linesByDevice[$l['id']][] = $l;
}
$attendantsByDevice = [];
foreach (q($db, 'SELECT id, attendantid, keyword, value, label, type FROM polycom_device_attendants', $warnings) as $a) {
    $attendantsByDevice[$a['id']][] = $a;
}
foreach (q($db, 'SELECT id, name, mac, model, version, lastip FROM polycom_devices', $warnings) as $d) {
    $keys = [];
    foreach ($attendantsByDevice[$d['id']] ?? [] as $a) {
        $keys[(int)$a['attendantid']] = [
            'position' => (int)$a['attendantid'],
            'label' => $a['label'],
            'type' => $a['keyword'],
            'value' => $a['value'],
        ];
    }
    ksort($keys); $keys = array_values($keys);
    $line = ($linesByDevice[$d['id']][0] ?? null);
    $manifest['phones'][] = [
        'mac' => strtolower($d['mac']),
        'model' => $d['model'],
        'lastIp' => $d['lastip'],
        'line' => $line ? $line['deviceid'] : null,
        'keys' => $keys,
    ];
}

// Voicemail spool: INBOX + Old WAV/msg envelopes per extension, staged into the tarball.
$spool = '/var/spool/asterisk/voicemail';
$vmCount = 0;
foreach ($manifest['extensions'] as $i => $e) {
    $ctx = $e['voicemailContext'] ?: 'default';
    $dir = $spool . '/' . $ctx . '/' . $e['number'];
    if (!is_dir($dir)) continue;
    $n = 0;
    foreach (['INBOX', 'Old'] as $box) {
        foreach (glob($dir . '/' . $box . '/msg*.{wav,txt,WAV,g722,ulaw,alaw,gsm}', GLOB_BRACE) ?: [] as $f) {
            $staged['files/voicemail/' . $e['number'] . '/' . $box . '/' . basename($f)] = $f;
            if (stripos(basename($f), '.txt') !== false) $n++;
        }
    }
    $manifest['extensions'][$i]['voicemailMessages'] = $n;
    $vmCount += $n;
}

$manifest['warnings'] = $warnings;

// Archive with the system tar: PharData holds the whole archive in memory while it builds,
// and a full voicemail spool (hundreds of MB) kills the process on small boxes. tar streams.
$stage = tempnam(sys_get_temp_dir(), 'tnpbx-stage');
unlink($stage);
mkdir($stage . '/files/sounds', 0770, true);
foreach ($staged as $tarPathRel => $src) {
    $dest = $stage . '/' . $tarPathRel;
    @mkdir(dirname($dest), 0770, true);
    if (!@copy($src, $dest)) {
        $warnings[] = "Could not read $src (staged as $tarPathRel).";
    }
}
$manifest['warnings'] = $warnings;
file_put_contents($stage . '/manifest.json', json_encode($manifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));
exec('tar -czf ' . escapeshellarg($output) . ' -C ' . escapeshellarg($stage) . ' manifest.json files 2>&1', $tarOut, $tarCode);
foreach (new RecursiveIteratorIterator(new RecursiveDirectoryIterator($stage, FilesystemIterator::SKIP_DOTS)) as $f) @unlink($f);
@rmdir($stage . '/files/sounds'); @rmdir($stage . '/files'); @rmdir($stage); // best-effort
if ($tarCode !== 0) {
    fwrite(STDERR, "tar failed (" . implode("\n", $tarOut) . ")\n");
    exit(1);
}

echo "Exported to $output\n";
echo '  extensions: ' . count($manifest['extensions']) . ", with voicemail messages: $vmCount\n";
echo '  trunks: ' . count($manifest['trunks']) . " (import lands them DISABLED, D170)\n";
echo '  outbound route patterns: ' . count($manifest['outboundRoutes']) . "\n";
echo '  inbound routes: ' . count($manifest['inboundRoutes']) . "\n";
echo '  sounds: ' . count($manifest['sounds']) . "\n";
echo '  phones: ' . count($manifest['phones']) . "\n";
echo count($warnings) ? '  warnings: ' . count($warnings) . "\n" : '';
exit(0);
