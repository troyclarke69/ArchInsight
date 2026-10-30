"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Activity, ArrowLeft, ArrowRight, Bell, Check, CircleAlert, FileText, GitPullRequest, Play, RefreshCw, ShieldCheck, Sparkles } from "lucide-react";

const api = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Repository = { id: string; name: string; applications: { name: string }[] };
type Overview = { repositories: number; monitoredRepositories: number; architectureHealth: number; technologyRisk: number; modernizationProgress: number; policyCompliance: number; policyCount: number; failedPolicies: number; warningPolicies: number; openAlerts: number; criticalAlerts: number; biggestRisks: Alert[]; trends: { repositoryAnalysisId: string; repositoryName: string; createdDate: string; riskScore: number; modernizationScore: number; technicalDebt: number; cloudReadiness: number; securityScore: number; consistencyScore: number; maturityIndex: number }[]; latestSnapshots: { repositoryAnalysisId: string; repositoryName: string; createdDate: string; applicationCount: number; serviceCount: number; dependencyCount: number; riskScore: number; maturityIndex: number }[] };
type Alert = { id: string; repositoryAnalysisId: string; repositoryName: string; category: string; severity: string; title: string; description: string; affectedAsset: string; createdDate: string; status?: string };
type Policy = { id: string; name: string; description: string; category: string; severity: string; enabled: boolean };
type Standard = { id: string; technology: string; versionPattern: string; lifecycle: string; rationale: string; reviewDate: string | null };
type Detail = { repository: { id: string; name: string }; snapshots: { id: string; createdDate: string; riskScore: number; modernizationScore: number; technicalDebt: number; cloudReadiness: number; securityScore: number; consistencyScore: number; maturityIndex: number; applicationCount: number; serviceCount: number; dependencyCount: number }[]; alerts: Alert[]; evaluations: { id: string; architectureSnapshotId: string; policyName: string; asset: string; status: string; evidence: string }[]; decisions: Decision[]; pullRequests: { id: string; pullRequestNumber: number; title: string; riskLevel: string; riskScore: number; summary: string; reviewedAt: string }[] };
type Decision = { id: string; title: string; context: string; decision: string; consequences: string; status: string; createdDate: string };
type PullRequestResult = { riskScore: number; riskLevel: string; summary: string; findings: { category: string; severity: string; title: string; description: string; affectedPath: string }[] };
async function request<T>(url: string, init?: RequestInit): Promise<T> { const response = await fetch(url, { cache: "no-store", ...init }); if (!response.ok) { let message = response.statusText; try { const body = await response.json(); message = body.error || body.detail || body.title || message; } catch { /* response has no JSON body */ } throw new Error(message); } return response.json(); }

export default function MonitoringPage() {
  const [repositories, setRepositories] = useState<Repository[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [overview, setOverview] = useState<Overview | null>(null);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [standards, setStandards] = useState<Standard[]>([]);
  const [scanFile, setScanFile] = useState<File | null>(null);
  const [prNumber, setPrNumber] = useState("1");
  const [prTitle, setPrTitle] = useState("");
  const [prHead, setPrHead] = useState("");
  const [prDiff, setPrDiff] = useState("");
  const [prResult, setPrResult] = useState<PullRequestResult | null>(null);
  const [decision, setDecision] = useState({ title: "", context: "", decision: "", consequences: "", status: "Proposed" });
  const [question, setQuestion] = useState("Which governance risks should we address first?");
  const [advice, setAdvice] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const refresh = useCallback(async () => {
    try {
      const [inventory, stats, policyList, standardList] = await Promise.all([
        request<Repository[]>(`${api}/api/inventory`), request<Overview>(`${api}/api/governance/overview`),
        request<Policy[]>(`${api}/api/governance/policies`), request<Standard[]>(`${api}/api/governance/standards`),
      ]);
      setRepositories(inventory); setOverview(stats); setPolicies(policyList); setStandards(standardList);
      const id = selectedId || inventory[0]?.id || "";
      if (id) { setSelectedId(id); setDetail(await request<Detail>(`${api}/api/governance/repositories/${id}`)); }
      else setDetail(null);
      setError("");
    } catch (e) { setError(e instanceof Error ? e.message : "Could not load governance monitoring data."); }
    finally { setLoading(false); }
  }, [selectedId]);
  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => { if (!selectedId) return; void request<Detail>(`${api}/api/governance/repositories/${selectedId}`).then(setDetail).catch((e) => setError(e instanceof Error ? e.message : "Unable to load repository monitoring.")); }, [selectedId]);

  const rescan = async () => {
    if (!selectedId || !scanFile) { setError("Select a repository and ZIP snapshot first."); return; }
    setBusy(true); setError(""); setNotice("");
    try { const body = new FormData(); body.append("file", scanFile); const result = await request<{ snapshot: { riskScore: number }; newAlerts: Alert[]; policyResults: number }>(`${api}/api/repositories/${selectedId}/monitor`, { method: "POST", body }); setNotice(`Snapshot recorded. Risk score ${result.snapshot.riskScore}/100; ${result.newAlerts.length} drift alert(s) and ${result.policyResults} policy results.`); setScanFile(null); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : "Repository scan failed."); } finally { setBusy(false); }
  };
  const setAlertStatus = async (alert: Alert, status: string) => { try { await request(`${api}/api/governance/alerts/${alert.id}/status`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status }) }); await refresh(); } catch (e) { setError(e instanceof Error ? e.message : "Unable to update alert."); } };
  const togglePolicy = async (policy: Policy) => { try { await request(`${api}/api/governance/policies/${policy.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enabled: !policy.enabled }) }); await refresh(); } catch (e) { setError(e instanceof Error ? e.message : "Unable to update policy."); } };
  const reviewPullRequest = async () => { if (!selectedId) return; setBusy(true); setError(""); try { const result = await request<PullRequestResult>(`${api}/api/repositories/${selectedId}/pull-requests/review`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ pullRequestNumber: Number(prNumber), title: prTitle, headRevision: prHead, diff: prDiff }) }); setPrResult(result); await refresh(); } catch (e) { setError(e instanceof Error ? e.message : "Pull request review failed."); } finally { setBusy(false); } };
  const saveDecision = async () => { if (!selectedId) return; setBusy(true); setError(""); try { await request(`${api}/api/governance/decisions`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ repositoryAnalysisId: selectedId, ...decision }) }); setDecision({ title: "", context: "", decision: "", consequences: "", status: "Proposed" }); setNotice("Architecture decision recorded."); await refresh(); } catch (e) { setError(e instanceof Error ? e.message : "Unable to save architecture decision."); } finally { setBusy(false); } };
  const askAdvisor = async () => { if (!selectedId || !question.trim()) return; setBusy(true); setError(""); try { const result = await request<{ answer: string }>(`${api}/api/governance/advice`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ repositoryAnalysisId: selectedId, question }) }); setAdvice(result.answer); } catch (e) { setError(e instanceof Error ? e.message : "Governance advisor is unavailable."); } finally { setBusy(false); } };

  const riskAlerts = detail?.alerts.filter((a) => a.status === "Open") ?? [];
  const trend = detail?.snapshots.slice().sort((a, b) => Date.parse(a.createdDate) - Date.parse(b.createdDate)).slice(-12) ?? [];
  const trendPoints = trend.length < 2 ? "0,45 100,45" : trend.map((x, i) => `${i * 100 / (trend.length - 1)},${90 - x.maturityIndex * .8}`).join(" ");

  return <div className="monitor-page">
    <header className="monitor-top"><Link href="/" className="sim-back"><ArrowLeft size={15} /> Overview</Link><span>ARCHITECTURE GOVERNANCE</span></header>
    <main className="monitor-main">
      <div className="monitor-heading"><div><div className="eyebrow"><span className="live-dot" />CONTINUOUS ARCHITECTURE GOVERNANCE</div><h1>Monitoring & governance</h1><p>Track architecture health, policy compliance, drift, and decisions across repository snapshots.</p></div><button className="button secondary" onClick={() => void refresh()}><RefreshCw size={14} /> Refresh</button></div>
      {error && <div className="monitor-message error">{error}<button onClick={() => setError("")}>Dismiss</button></div>}{notice && <div className="monitor-message success">{notice}</div>}
      <section className="monitor-kpis">
        {[{ label: "Architecture health", value: overview?.architectureHealth ?? 0, foot: `${overview?.monitoredRepositories ?? 0} monitored repositories`, icon: Activity, color: "blue" }, { label: "Technology risk", value: overview?.technologyRisk ?? 0, foot: `${overview?.openAlerts ?? 0} open drift alerts`, icon: CircleAlert, color: "orange" }, { label: "Modernization", value: overview?.modernizationProgress ?? 0, foot: "Relative baseline score", icon: ArrowRight, color: "green" }, { label: "Policy compliance", value: overview?.policyCompliance ?? 0, foot: `${overview?.failedPolicies ?? 0} failed · ${overview?.warningPolicies ?? 0} warnings`, icon: ShieldCheck, color: "purple" }, { label: "Maturity index", value: overview?.architectureHealth ?? 0, foot: "Governance · cloud · security · consistency", icon: Check, color: "green" }].map((x) => { const Icon = x.icon; return <article className="monitor-kpi" key={x.label}><div><span>{x.label}</span><i className={x.color}><Icon size={15} /></i></div><strong>{loading ? "—" : x.value}<small>/100</small></strong><p>{x.foot}</p></article>; })}
      </section>
      <section className="monitor-main-grid">
        <article className="monitor-card monitor-trend"><div className="monitor-card-heading"><div><h2>Architecture health over time</h2><p>Snapshots create an auditable trend; assessments remain immutable.</p></div><label>Repository<select value={selectedId} onChange={(e) => setSelectedId(e.target.value)}>{repositories.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label></div>
          {trend.length ? <><svg className="monitor-chart" viewBox="0 0 100 100" preserveAspectRatio="none"><polyline points={trendPoints} fill="none" stroke="#3978ed" strokeWidth="2.3" vectorEffect="non-scaling-stroke" />{trend.map((x, i) => <circle key={x.id} cx={trend.length < 2 ? 50 : i * 100 / (trend.length - 1)} cy={90 - x.maturityIndex * .8} r="1.5" fill="#3978ed" vectorEffect="non-scaling-stroke" />)}</svg><div className="monitor-trend-legend"><span><i className="health" />Maturity index</span><span><i className="risk" />Technology risk</span><span>{trend.length} snapshots · latest {new Date(trend.at(-1)!.createdDate).toLocaleDateString()}</span></div><div className="monitor-metric-strip">{["Modernization", "Technical debt", "Cloud readiness", "Security", "Consistency"].map((label, i) => { const last = trend.at(-1)!; const val = [last.modernizationScore, last.technicalDebt, last.cloudReadiness, last.securityScore, last.consistencyScore][i]; return <div key={label}><span>{label}</span><b>{val}</b></div>; })}</div></> : <div className="monitor-empty">No snapshots yet. Record a scan below to start a governance history.</div>}
        </article>
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2>Technology lifecycle</h2><p>Local reference standards used by governance rules.</p></div></div><div className="standard-list">{standards.map((s) => <div key={s.id}><span className={`lifecycle ${s.lifecycle.toLowerCase()}`}>{s.lifecycle}</span><b>{s.technology}</b><code>{s.versionPattern}</code><small>{s.rationale}</small></div>)}</div></article>
      </section>
      <section className="monitor-two-col">
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><Bell size={14} /> Architecture review queue</h2><p>Policy violations, drift alerts, and risk regressions.</p></div><span className="monitor-pill">{riskAlerts.length} open</span></div>
          {riskAlerts.length ? <div className="governance-alert-list">{riskAlerts.map((a) => <div className="governance-alert" key={a.id}><span className={`alert-severity ${a.severity.toLowerCase()}`}>{a.severity}</span><div><b>{a.title}</b><span>{a.repositoryName || detail?.repository.name} · {a.category} · {a.affectedAsset}</span><p>{a.description}</p></div><select value={a.status || "Open"} onChange={(e) => void setAlertStatus(a, e.target.value)}><option>Open</option><option>Acknowledged</option><option>Resolved</option></select></div>)}</div> : <div className="monitor-empty">No open drift alerts for this repository.</div>}
        </article>
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><ShieldCheck size={14} /> Architecture policies</h2><p>Enable or disable deterministic fitness functions.</p></div><span className="monitor-pill">{overview?.policyCount ?? 0} enabled</span></div><div className="policy-list">{policies.map((p) => <label key={p.id} className="policy-row"><div><b>{p.name}</b><span>{p.category} · {p.severity} · {p.description}</span></div><input type="checkbox" checked={p.enabled} onChange={() => void togglePolicy(p)} aria-label={`Enable ${p.name}`} /></label>)}</div></article>
      </section>
      <section className="monitor-two-col">
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><Play size={14} /> Record a new architecture snapshot</h2><p>Upload a new ZIP state for drift comparison and policy evaluation.</p></div></div><div className="monitor-form"><label>Current repository<select value={selectedId} onChange={(e) => setSelectedId(e.target.value)}>{repositories.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label><label>Updated repository ZIP<input type="file" accept=".zip,application/zip" onChange={(e) => setScanFile(e.target.files?.[0] ?? null)} /></label><button className="button primary" onClick={() => void rescan()} disabled={busy || !selectedId || !scanFile}><Play size={14} />{busy ? "Scanning…" : "Scan and compare"}</button></div><p className="monitor-note">Scans are uploaded on demand in this demo; there is no background GitHub scheduler.</p></article>
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><Sparkles size={14} /> AI governance advisor</h2><p>Answers use the selected repository’s latest snapshot, open alerts, portfolio and assessment evidence.</p></div></div><textarea className="monitor-textarea compact" value={question} onChange={(e) => setQuestion(e.target.value)} /><button className="button secondary" onClick={() => void askAdvisor()} disabled={busy || !selectedId}><Sparkles size={14} /> Ask advisor</button>{advice && <blockquote className="governance-advice">{advice}</blockquote>}</article>
      </section>
      <section className="monitor-two-col">
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><FileText size={14} /> Architecture decisions</h2><p>Capture context, decision, and consequences as lightweight ADRs.</p></div></div><div className="monitor-form"><label>Title<input value={decision.title} onChange={(e) => setDecision({ ...decision, title: e.target.value })} /></label><label>Context<textarea value={decision.context} onChange={(e) => setDecision({ ...decision, context: e.target.value })} /></label><label>Decision<textarea value={decision.decision} onChange={(e) => setDecision({ ...decision, decision: e.target.value })} /></label><label>Consequences<textarea value={decision.consequences} onChange={(e) => setDecision({ ...decision, consequences: e.target.value })} /></label><button className="button secondary" onClick={() => void saveDecision()} disabled={busy || !selectedId}><FileText size={14} /> Save decision</button></div><div className="decision-list">{detail?.decisions.map((d) => <div key={d.id}><span>{d.status}</span><b>{d.title}</b><p>{d.decision}</p></div>)}</div></article>
        <article className="monitor-card"><div className="monitor-card-heading"><div><h2><GitPullRequest size={14} /> Pull request architecture review</h2><p>Paste a unified diff for deterministic impact and risk checks.</p></div></div><div className="monitor-form pr-fields"><label>PR number<input type="number" min="1" value={prNumber} onChange={(e) => setPrNumber(e.target.value)} /></label><label>Title<input value={prTitle} onChange={(e) => setPrTitle(e.target.value)} /></label><label>Head revision<input value={prHead} onChange={(e) => setPrHead(e.target.value)} placeholder="optional commit SHA" /></label><label>Unified diff<textarea className="monitor-diff" value={prDiff} onChange={(e) => setPrDiff(e.target.value)} maxLength={300000} /></label><button className="button secondary" onClick={() => void reviewPullRequest()} disabled={busy || !selectedId}><GitPullRequest size={14} /> Review diff</button></div>{prResult && <div className="pr-result"><div><span className={`alert-severity ${prResult.riskLevel.toLowerCase()}`}>{prResult.riskLevel}</span><b>Change risk {prResult.riskScore}/100</b></div><p>{prResult.summary}</p>{prResult.findings.map((f, i) => <small key={i}>{f.severity} · {f.title} · {f.affectedPath}</small>)}</div>}<p className="monitor-note">This reviews a supplied diff; it does not connect to GitHub or replace source, security, or compatibility review.</p></article>
      </section>
      <div className="monitor-footer"><span>{overview?.criticalAlerts ?? 0} critical alerts across the portfolio</span><Link href="/simulator">Open transformation simulator <ArrowRight size={13} /></Link></div>
    </main>
  </div>;
}
