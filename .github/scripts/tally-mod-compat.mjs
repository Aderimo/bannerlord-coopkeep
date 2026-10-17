// "mod-compat" etiketli issue'ları baştan sayar ve compat/mods.json'daki
// works/broken sayılarını ve durumları günceller.
//
// Her çalıştırmada tüm açık+kapalı bildirimler yeniden sayılır; aynı girdiyle
// iki kez çalıştırmak aynı dosyayı üretir (idempotent).
//
// Kullanım:
//   node tally-mod-compat.mjs [--file compat/mods.json] [--issues fixture.json] [--dry-run]
//
// --issues verilmezse issue'lar GitHub API'den çekilir (GITHUB_TOKEN ve
// GITHUB_REPOSITORY ortam değişkenleri gerekir). Bağımlılık yok; Node 18+.

import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

export const LABEL = 'mod-compat';
export const IGNORED_LABELS = ['invalid', 'spam'];
// works - broken bu farka ulaşınca durum otomatik değişir.
export const THRESHOLD = 3;

const KEY_ORDER = ['id', 'name', 'status', 'coop', 'note', 'workshop', 'nexus', 'category', 'works', 'broken', 'idGuess'];
const ID_PATTERN = /^[\p{L}\p{N}_.\-' ()+&!:]{1,100}$/u;

// ---- JSONC ---------------------------------------------------------------

/** Dizelerin içine dokunmadan // ve /* *\/ yorumlarını ve sondaki virgülleri siler. */
export function stripJsonc(text) {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const c = text[i], n = text[i + 1];
    if (c === '"') {
      let j = i + 1;
      while (j < text.length && text[j] !== '"') j += text[j] === '\\' ? 2 : 1;
      out += text.slice(i, j + 1);
      i = j;
    } else if (c === '/' && n === '/') {
      while (i < text.length && text[i] !== '\n') i++;
      out += '\n';
    } else if (c === '/' && n === '*') {
      const end = text.indexOf('*/', i + 2);
      i = end < 0 ? text.length : end + 1;
    } else {
      out += c;
    }
  }
  return out.replace(/,(\s*[\]}])/g, '$1');
}

export function parseJsonc(text) {
  return JSON.parse(stripJsonc(text));
}

function formatEntry(e) {
  const keys = [...KEY_ORDER.filter(k => k in e), ...Object.keys(e).filter(k => !KEY_ORDER.includes(k))];
  return '{ ' + keys.filter(k => e[k] !== undefined && e[k] !== null).map(k => `${JSON.stringify(k)}: ${JSON.stringify(e[k])}`).join(', ') + ' }';
}

export function sortMods(mods) {
  const label = m => (m.name ?? m.id);
  return mods.sort((a, b) => label(a).localeCompare(label(b), 'en', { sensitivity: 'base' }) || a.id.localeCompare(b.id));
}

/**
 * Dosyanın "mods" dizisine kadar olan kısmını (üst yorum bloğu dahil) aynen
 * korur, diziyi her satırda bir kayıt olacak şekilde yeniden yazar.
 */
export function serialize(originalText, mods) {
  const m = originalText.match(/^([\s\S]*?"mods"\s*:\s*\[)/m);
  if (!m) throw new Error('"mods" dizisi bulunamadı');
  const nl = originalText.includes('\r\n') ? '\r\n' : '\n';
  const body = mods.map(formatEntry).map(s => '    ' + s).join(',' + nl);
  return m[1] + nl + body + nl + '  ]' + nl + '}' + nl;
}

// ---- Issue formu --------------------------------------------------------

/** Issue form gövdesini "### Başlık" bölümlerine ayırır. */
export function parseForm(body) {
  const fields = {};
  const parts = String(body ?? '').replace(/\r\n/g, '\n').split(/^###[ \t]+/m).slice(1);
  for (const part of parts) {
    const nl = part.indexOf('\n');
    const header = (nl < 0 ? part : part.slice(0, nl)).trim().toLowerCase();
    let value = (nl < 0 ? '' : part.slice(nl + 1)).trim();
    if (value === '_No response_') value = '';
    value = value.replace(/^```\w*\n?|\n?```$/g, '').replace(/^`+|`+$/g, '').trim();
    if (header.startsWith('mod id')) fields.modId = value.split('\n')[0].trim();
    else if (header.startsWith('mod name')) fields.modName = value.split('\n')[0].trim();
    else if (header.startsWith('result')) fields.result = value;
    else if (header.startsWith('coop version')) fields.coop = value.split('\n')[0].trim();
  }
  return fields;
}

export function normalizeResult(value) {
  const v = String(value ?? '').toLowerCase().replace(/[^a-zçğıöşü]/g, '');
  if (['works', 'work', 'working', 'yes', 'çalışıyor', 'calisiyor'].includes(v)) return 'works';
  if (['broken', 'broke', 'notworking', 'no', 'crash', 'çalışmıyor', 'calismiyor'].includes(v)) return 'broken';
  if (['clientonly', 'client', 'yalnızcaoyuncularda'].includes(v)) return 'clientonly';
  return null;
}

// ---- Sayım --------------------------------------------------------------

/**
 * Geçerli bildirimleri döndürür: kullanıcı+mod başına yalnızca en sonuncusu.
 * invalid/spam etiketliler, PR'lar, botlar ve ayrıştırılamayanlar atlanır.
 */
export function collectReports(issues) {
  const latest = new Map();
  const skipped = [];
  for (const issue of issues) {
    const labels = (issue.labels ?? []).map(l => (typeof l === 'string' ? l : l.name).toLowerCase());
    if (issue.pull_request || !labels.includes(LABEL)) continue;
    if (labels.some(l => IGNORED_LABELS.includes(l))) { skipped.push([issue.number, 'invalid/spam']); continue; }
    if (issue.user?.type === 'Bot') { skipped.push([issue.number, 'bot']); continue; }
    const f = parseForm(issue.body);
    const result = normalizeResult(f.result);
    if (!f.modId || !ID_PATTERN.test(f.modId)) { skipped.push([issue.number, 'mod id okunamadı']); continue; }
    if (!result) { skipped.push([issue.number, 'sonuç okunamadı']); continue; }
    const user = (issue.user?.login ?? `#${issue.number}`).toLowerCase();
    const key = `${user}|${f.modId.toLowerCase()}`;
    const report = { number: issue.number, user, modId: f.modId, modName: ID_PATTERN.test(f.modName ?? '') ? f.modName : null, result, created: issue.created_at ?? '' };
    const prev = latest.get(key);
    if (!prev || report.created > prev.created || (report.created === prev.created && report.number > prev.number)) latest.set(key, report);
  }
  return { reports: [...latest.values()].sort((a, b) => a.number - b.number), skipped };
}

/** Manuel kayıtlar ("coop" alanı olanlar) durumunu asla otomatik değiştirmez. */
export const isManual = e => typeof e.coop === 'string' && e.coop.trim() !== '';

export function applyTally(mods, reports) {
  const byId = new Map(mods.map(m => [m.id.toLowerCase(), m]));
  const counts = new Map();
  const added = [], renamed = [];

  for (const r of reports) {
    let entry = byId.get(r.modId.toLowerCase());
    if (!entry && r.modName) {
      // Kimliği tahmin olan kayıt, gerçek kimlikle gelen ilk bildirimde düzeltilir.
      entry = mods.find(m => m.idGuess && (m.name ?? m.id).toLowerCase() === r.modName.toLowerCase());
      if (entry) {
        renamed.push([entry.id, r.modId]);
        byId.delete(entry.id.toLowerCase());
        entry.id = r.modId;
        delete entry.idGuess;
        byId.set(entry.id.toLowerCase(), entry);
      }
    }
    if (!entry) {
      entry = { id: r.modId, name: r.modName ?? r.modId, status: 'Unknown', works: 0, broken: 0 };
      mods.push(entry);
      byId.set(entry.id.toLowerCase(), entry);
      added.push(entry.id);
    }
    const c = counts.get(entry) ?? { works: 0, broken: 0, clientonly: 0 };
    c[r.result]++;
    counts.set(entry, c);
  }

  const statusChanges = [];
  for (const m of mods) {
    const c = counts.get(m) ?? { works: 0, broken: 0 };
    m.works = c.works;
    m.broken = c.broken;
    m.status ??= 'Unknown';
    if (isManual(m)) continue;
    let next = m.status;
    if (m.works - m.broken >= THRESHOLD) next = 'Works';
    else if (m.broken - m.works >= THRESHOLD) next = 'Broken';
    if (next !== m.status) { statusChanges.push([m.id, m.status, next]); m.status = next; }
  }
  sortMods(mods);
  return { added, renamed, statusChanges };
}

// ---- GitHub -------------------------------------------------------------

async function fetchIssues(repo, token) {
  const all = [];
  for (let page = 1; ; page++) {
    const url = `https://api.github.com/repos/${repo}/issues?labels=${LABEL}&state=all&per_page=100&page=${page}`;
    const res = await fetch(url, {
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' },
    });
    if (!res.ok) throw new Error(`GitHub API ${res.status}: ${await res.text()}`);
    const batch = await res.json();
    all.push(...batch);
    if (batch.length < 100) return all;
  }
}

// ---- Giriş noktası ------------------------------------------------------

function arg(name) {
  const i = process.argv.indexOf(name);
  return i > 0 ? process.argv[i + 1] : undefined;
}

async function main() {
  const file = arg('--file') ?? 'compat/mods.json';
  const fixture = arg('--issues');
  const dryRun = process.argv.includes('--dry-run');

  let issues;
  if (fixture) {
    issues = JSON.parse(fs.readFileSync(fixture, 'utf8'));
  } else {
    const { GITHUB_TOKEN: token, GITHUB_REPOSITORY: repo } = process.env;
    if (!token || !repo) throw new Error('GITHUB_TOKEN ve GITHUB_REPOSITORY gerekli (ya da --issues <fixture.json>)');
    issues = await fetchIssues(repo, token);
  }

  const original = fs.readFileSync(file, 'utf8');
  const doc = parseJsonc(original);
  const { reports, skipped } = collectReports(issues);
  const { added, renamed, statusChanges } = applyTally(doc.mods, reports);
  const text = serialize(original, doc.mods);
  parseJsonc(text); // yazmadan önce sonucun geçerli olduğundan emin ol

  console.log(`issue: ${issues.length}, geçerli bildirim: ${reports.length}, atlanan: ${skipped.length}, kayıt: ${doc.mods.length}`);
  for (const [n, why] of skipped) console.log(`  atlandı #${n}: ${why}`);
  for (const id of added) console.log(`  yeni kayıt: ${id}`);
  for (const [from, to] of renamed) console.log(`  kimlik düzeltildi: ${from} -> ${to}`);
  for (const [id, from, to] of statusChanges) console.log(`  durum: ${id} ${from} -> ${to}`);

  const changed = text !== original;
  console.log(changed ? 'Değişiklik var.' : 'Değişiklik yok.');
  if (changed && !dryRun) fs.writeFileSync(file, text);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\n`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === fs.realpathSync(process.argv[1])) {
  main().catch(err => { console.error(err); process.exit(1); });
}
