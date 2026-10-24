"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { Activity, ArrowDownRight, ArrowRight, ArrowUpRight, Bell, Boxes, Check, ChevronDown, CircleHelp, CloudUpload, Command, Database, Layers3, LayoutDashboard, LockKeyhole, Menu, Plus, Search, Settings2, ShieldAlert, Sparkles, X } from "lucide-react";

type AssessmentType = "Modernization" | "DataMaturity" | "ServiceConsistency";
type Status = "Pending" | "Running" | "Completed" | "Failed";
type Severity = "Critical" | "High" | "Medium" | "Low";
type Finding = { title: string; description: string; category: string; severity: Severity; recommendation: string };
type Rec = { priority: number; title: string; description: string; timeframe: string };
type ScoreCard = { dimension: string; score: number };
type AIStatus = "Pending" | "Running" | "Completed" | "Failed" | "NotConfigured";
type AIRec = { priority: number; title: string; description: string; businessBenefit: string; estimatedComplexity: string; expectedOutcome: string };
type AIPlan = { sequence: number; period: string; activityType: string; title: string; description: string; expectedOutcome: string };
type Insight = { id: string; status: AIStatus; executiveSummary: string | null; currentState: string | null; keyRisks: string[]; businessImpact: string | null; operationalRisk: string | null; technicalDebtImpact: string | null; cloudReadiness: string | null; deliveryVelocityImpact: string | null; dataAndReportingImpact: string | null; riskNarrative: string | null; recommendedStrategy: string | null; recommendedNextSteps: string[]; expectedOutcome: string | null; confidence: "Low" | "Medium" | "High" | null; createdDate: string; generatedAt: string | null; modelName: string | null; promptVersion: string | null; tokenUsage: number | null; errorMessage: string | null; recommendations: AIRec[]; roadmap: AIPlan[] };
type AIQuestion = { id: string; question: string; answer: string | null; status: "Pending" | "Running" | "Completed" | "Failed" | "NotConfigured"; askedAt: string; errorMessage: string | null };
type Summary = { id: string; name: string; organizationName: string; assessmentType: AssessmentType; status: Status; createdDate: string; score: number | null; recommendation: string | null; findingsCount: number };
type Detail = Summary & { completedDate: string | null; executiveSummary: string | null; applicationCount: number; errorMessage: string | null; scoreCards: ScoreCard[]; findings: Finding[]; recommendations: Rec[]; artifactNames: string[]; insights: Insight[]; aiQuestions: AIQuestion[] };
type Comparison = { baselineId: string; baselineName: string; comparisonId: string; comparisonName: string; assessmentType: AssessmentType; baselineScore: number; comparisonScore: number; scoreChange: number; scoreDirection: string; resolvedFindings: { title: string; category: string; severity: Severity }[]; newFindings: { title: string; category: string; severity: Severity }[]; improvementAreas: string[]; regressionAreas: string[]; riskMovement: { severity: Severity; baselineCount: number; comparisonCount: number; change: number }[]; aiExplanation: string | null; aiExplanationError: string | null };
type IntakeFile = { file: File; id: string };

const apiBase = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
const typeOptions: { id: AssessmentType; label: string; description: string; icon: typeof Boxes; artifactType: string }[] = [
  { id: "Modernization", label: "Modernization", description: "Technology health & next steps", icon: Boxes, artifactType: "TechnologyInventory" },
  { id: "DataMaturity", label: "Data maturity", description: "Governance, platform & AI readiness", icon: Database, artifactType: "DataPlatformInventory" },
  { id: "ServiceConsistency", label: "Service consistency", description: "API contracts & shared patterns", icon: Layers3, artifactType: "ServiceManifest" },
];
const typeLabel: Record<AssessmentType, string> = { Modernization: "Modernization", DataMaturity: "Data maturity", ServiceConsistency: "Service consistency" };
const severityOrder: Record<Severity, number> = { Critical: 0, High: 1, Medium: 2, Low: 3 };
const severityClass: Record<Severity, string> = { Critical: "critical", High: "high", Medium: "medium", Low: "low" };

async function readError(response: Response) {
  try { const body = await response.json(); return body.error || body.detail || body.title || response.statusText || "Request failed"; }
  catch { return response.statusText || "Request failed"; }
}

function ScoreRing({ score }: { score: number | null }) {
  const value = score ?? 0;
  return <div className={`score-ring ${score === null ? "empty" : ""}`} style={{ "--score": `${value * 3.6}deg` } as React.CSSProperties}><div><strong>{score ?? "—"}</strong><span>{score === null ? "No score" : "/ 100"}</span></div></div>;
}

function TrendSpark({ values }: { values: number[] }) {
  const points = values.length < 2 ? "0,22 100,22" : values.map((value, index) => `${index * 100 / (values.length - 1)},${40 - value * 0.34}`).join(" ");
  return <svg className="trend-spark" viewBox="0 0 100 42" preserveAspectRatio="none" aria-hidden="true"><polyline points={points} fill="none" stroke="#3978ed" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" /></svg>;
}

export default function Home() {
  const [assessments, setAssessments] = useState<Summary[]>([]);
  const [details, setDetails] = useState<Record<AssessmentType, Detail | null>>({ Modernization: null, DataMaturity: null, ServiceConsistency: null });
  const [loading, setLoading] = useState(true);
  const [connectionError, setConnectionError] = useState("");
  const [modalOpen, setModalOpen] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [name, setName] = useState("Architecture assessment");
  const [organization, setOrganization] = useState("Northstar Group");
  const [assessmentType, setAssessmentType] = useState<AssessmentType>("Modernization");
  const [files, setFiles] = useState<IntakeFile[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState("");
  const [uploadError, setUploadError] = useState("");
  const [baselineId, setBaselineId] = useState("");
  const [comparisonId, setComparisonId] = useState("");
  const [comparison, setComparison] = useState<Comparison | null>(null);
  const [compareError, setCompareError] = useState("");
  const [comparing, setComparing] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const refresh = useCallback(async () => {
    try {
      const response = await fetch(`${apiBase}/api/assessments`, { cache: "no-store" });
      if (!response.ok) throw new Error(await readError(response));
      const list: Summary[] = await response.json();
      setAssessments(list);
      setConnectionError("");
      const latest: Partial<Record<AssessmentType, Summary>> = {};
      for (const type of ["Modernization", "DataMaturity", "ServiceConsistency"] as const) latest[type] = list.find((item) => item.assessmentType === type);
      const values = await Promise.all(Object.entries(latest).map(async ([type, item]) => {
        if (!item) return [type, null] as const;
        const result = await fetch(`${apiBase}/api/assessments/${item.id}`, { cache: "no-store" });
        if (!result.ok) return [type, null] as const;
        return [type, await result.json() as Detail] as const;
      }));
      setDetails((current) => ({ ...current, ...Object.fromEntries(values) } as Record<AssessmentType, Detail | null>));
    } catch (error) {
      setConnectionError(error instanceof Error ? error.message : "Cannot connect to the assessment API.");
    } finally { setLoading(false); }
  }, []);

  const aiWorkInProgress = Object.values(details).some((detail) => detail?.insights[0]?.status === "Pending" || detail?.insights[0]?.status === "Running");
  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => {
    if (!assessments.some((item) => item.status === "Pending" || item.status === "Running") && !aiWorkInProgress) return;
    const timer = window.setInterval(() => { void refresh(); }, 1800);
    return () => window.clearInterval(timer);
  }, [assessments, aiWorkInProgress, refresh]);
  useEffect(() => {
    const completed = assessments.filter((item) => item.status === "Completed" && item.score !== null);
    if (completed.length >= 2) {
      if (!baselineId) setBaselineId(completed[1].id);
      if (!comparisonId) setComparisonId(completed[0].id);
    }
  }, [assessments, baselineId, comparisonId]);

  const findings = useMemo(() => Object.values(details).flatMap((detail) => detail?.findings ?? []).sort((a, b) => severityOrder[a.severity] - severityOrder[b.severity]), [details]);
  const recommendations = useMemo(() => Object.values(details).flatMap((detail) => detail?.recommendations ?? []).sort((a, b) => a.priority - b.priority).slice(0, 8), [details]);
  const criticalCount = findings.filter((finding) => finding.severity === "Critical").length;
  const highCount = findings.filter((finding) => finding.severity === "High").length;
  const statusCounts = assessments.reduce((count, item) => ({ ...count, [item.status]: (count[item.status] ?? 0) + 1 }), {} as Record<string, number>);
  const trends = typeOptions.map(({ id, label }) => {
    const history = assessments.filter((item) => item.assessmentType === id && item.status === "Completed" && item.score !== null).sort((a, b) => Date.parse(a.createdDate) - Date.parse(b.createdDate)).slice(-8);
    const first = history[0]; const last = history.at(-1);
    const change = first && last ? last.score! - first.score! : null;
    const improvementPercent = first?.score ? (change! / first.score) * 100 : null;
    const findingChange = first && last ? first.findingsCount - last.findingsCount : null;
    return { id, label, history, first, last, change, improvementPercent, findingChange };
  });
  const completedAssessments = assessments.filter((item) => item.status === "Completed" && item.score !== null);

  const runComparison = async () => {
    if (!baselineId || !comparisonId || baselineId === comparisonId) { setCompareError("Choose two different completed assessments."); return; }
    setComparing(true); setCompareError(""); setComparison(null);
    try {
      const query = new URLSearchParams(); query.append("id", baselineId); query.append("id", comparisonId);
      const response = await fetch(`${apiBase}/api/assessments/compare?${query.toString()}`, { cache: "no-store" });
      if (!response.ok) throw new Error(await readError(response));
      setComparison(await response.json());
    } catch (error) { setCompareError(error instanceof Error ? error.message : "Unable to compare assessments."); }
    finally { setComparing(false); }
  };

  const regenerateInsight = async (detail: Detail) => {
    try {
      const response = await fetch(`${apiBase}/api/assessments/${detail.id}/insights/regenerate`, { method: "POST" });
      if (!response.ok) throw new Error(await readError(response));
      setNotice("Insight generation queued. The deterministic score and recommendations are unchanged.");
      await refresh();
    } catch (error) { setNotice(error instanceof Error ? error.message : "Unable to queue insight generation."); }
  };

  const addFiles = (list: FileList | null) => {
    if (!list) return;
    const additions = Array.from(list).map((file) => ({ file, id: `${file.name}-${file.lastModified}-${Math.random()}` }));
    setFiles((current) => [...current, ...additions]);
    setUploadError("");
  };

  const startAssessment = async () => {
    if (!name.trim() || !organization.trim()) { setUploadError("Enter an assessment name and organization."); return; }
    if (files.length === 0) { setUploadError("Add at least one JSON artifact to run a deterministic assessment."); return; }
    if (files.some(({ file }) => !file.name.toLowerCase().endsWith(".json") || file.size > 2 * 1024 * 1024)) { setUploadError("Choose JSON files under 2 MB each."); return; }
    for (const { file } of files) {
      try { JSON.parse(await file.text()); }
      catch { setUploadError(`${file.name} is not valid JSON.`); return; }
    }
    setSubmitting(true); setUploadError("");
    try {
      const createdResponse = await fetch(`${apiBase}/api/assessments`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: name.trim(), organizationName: organization.trim(), assessmentType }) });
      if (!createdResponse.ok) throw new Error(await readError(createdResponse));
      const created: Summary = await createdResponse.json();
      const artifactType = typeOptions.find((option) => option.id === assessmentType)!.artifactType;
      for (const { file } of files) {
        const form = new FormData(); form.append("file", file);
        const uploadResponse = await fetch(`${apiBase}/api/artifacts?assessmentId=${created.id}&type=${artifactType}`, { method: "POST", body: form });
        if (!uploadResponse.ok) throw new Error(await readError(uploadResponse));
      }
      const runResponse = await fetch(`${apiBase}/api/assessments/${created.id}/run`, { method: "POST" });
      if (!runResponse.ok) throw new Error(await readError(runResponse));
      setModalOpen(false); setFiles([]); setName("Architecture assessment");
      setNotice(`${typeLabel[assessmentType]} assessment queued. Results will appear when the rules finish running.`);
      await refresh();
      window.setTimeout(() => setNotice(""), 6000);
    } catch (error) { setUploadError(error instanceof Error ? error.message : "Unable to start assessment."); }
    finally { setSubmitting(false); }
  };

  return <main className={`app-shell ${menuOpen ? "menu-open" : ""}`}>
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Command size={19} strokeWidth={2.2} /></div><span>archinsight<span className="brand-ai"> AI</span></span></div>
      <div className="workspace-picker"><div className="workspace-avatar">N</div><div className="workspace-copy"><b>Northstar Group</b><span>Enterprise workspace</span></div><ChevronDown size={15} /></div>
      <div className="nav-section-label">WORKSPACE</div>
      <nav className="main-nav" aria-label="Main navigation"><button className="nav-item active"><LayoutDashboard size={17} /><span>Overview</span></button>{typeOptions.map(({ id, label, icon: Icon }) => <a key={id} href={`#${id}`} onClick={() => setMenuOpen(false)} className="nav-item"><Icon size={17} /><span>{label}</span></a>)}</nav>
      <div className="nav-section-label assessments-label">ASSESSMENTS <button aria-label="Create assessment" onClick={() => setModalOpen(true)}><Plus size={15} /></button></div>
      {assessments.slice(0, 4).map((item) => <Link key={item.id} href={`/assessments/${item.id}`} className="assessment-link"><span className={`assessment-dot ${item.status === "Failed" ? "muted" : ""}`} /><span className="assessment-link-name">{item.name}</span><span className="assessment-date">{item.status === "Completed" ? item.score : item.status}</span></Link>)}
      <div className="sidebar-bottom"><div className="plan-card"><div className="plan-icon"><Sparkles size={16} /></div><b>Build a clearer picture</b><p>Run a rules-based assessment on your architecture artifacts.</p><button onClick={() => setModalOpen(true)}>Start an assessment <ArrowRight size={14} /></button></div><button className="nav-item"><CircleHelp size={17} /><span>Help center</span></button><button className="nav-item"><Settings2 size={17} /><span>Settings</span></button><div className="profile-row"><div className="profile-avatar">JD</div><div className="profile-copy"><b>Workspace admin</b><span>Northstar Group</span></div></div></div>
    </aside>

    <section className="main-column">
      <header className="topbar"><div className="breadcrumb"><span>Northstar Group</span><span className="crumb-sep">/</span><b>Overview</b></div><div className="top-actions"><div className="search-box"><Search size={16} /><span>Search assessments...</span><kbd>⌘ K</kbd></div><button className="icon-button" aria-label="Notifications"><Bell size={17} /></button><div className="top-avatar">JD</div></div></header>
      <div className="page-content">
        <div className="page-heading"><div><div className="eyebrow"><span className="live-dot" />ARCHITECTURE INTELLIGENCE</div><h1>Good morning, Jordan</h1><p>Live assessment results for your software landscape.</p></div><div className="heading-actions"><button className="button secondary" onClick={() => void refresh()}><Activity size={14} />Refresh data</button><button className="button primary" onClick={() => setModalOpen(true)}><Plus size={16} />New assessment</button></div></div>
        {connectionError && <div className="api-alert"><ShieldAlert size={17} /><div><b>Assessment API unavailable</b><span>{connectionError} Start the backend at localhost:5080 to load persisted results.</span></div></div>}
        {notice && <div className="notice"><Activity size={16} />{notice}<button onClick={() => setNotice("")} aria-label="Dismiss"><X size={15} /></button></div>}

        <div className="metrics-grid" id="Modernization">{typeOptions.map(({ id, label, icon: Icon }) => { const detail = details[id]; const status = assessments.find((item) => item.assessmentType === id)?.status; return <article className="metric-card" key={id}><div className="metric-top"><span>{label} score</span><span className="metric-icon blue"><Icon size={16} /></span></div><div className="metric-main"><ScoreRing score={detail?.score ?? null} /><div className="metric-context"><div className={`assessment-state ${status?.toLowerCase() ?? "empty"}`}>{loading ? "Loading" : status ?? "No assessment"}</div><span>{detail ? `${detail.findings.length} findings` : "Run an assessment to calculate a score"}</span></div></div><div className="metric-foot"><span>{detail ? id === "DataMaturity" ? `${detail.scoreCards.length} dimensions scored` : `${detail.applicationCount} systems assessed` : "No completed results"}</span><Link href={detail ? `/assessments/${detail.id}` : "#"} onClick={(event) => { if (!detail) { event.preventDefault(); setAssessmentType(id); setModalOpen(true); } }}>{detail ? "View results" : "Get started"}<ArrowRight size={14} /></Link></div></article>; })}
          <article className="risk-card"><div className="risk-heading"><span className="risk-shield"><ShieldAlert size={17} /></span><span>Open findings</span></div><div className="risk-score">{findings.length}<span className="risk-pill">Across all assessments</span></div><div className="risk-summary">{criticalCount} critical · {highCount} high priority</div><div className="risk-bar"><span className={criticalCount ? "filled-critical" : ""} /><span className={highCount ? "filled-high" : ""} /><span className={findings.some((f) => f.severity === "Medium") ? "filled-medium" : ""} /><span className={findings.some((f) => f.severity === "Low") ? "filled-low" : ""} /></div><div className="risk-foot"><span><b>{statusCounts.Completed ?? 0}</b> complete</span><span><b>{(statusCounts.Pending ?? 0) + (statusCounts.Running ?? 0)}</b> in progress</span></div></article>
        </div>

        <div className="section-heading"><div><h2>Assessment overview</h2><p>Persisted results from deterministic assessment engines</p></div><span className="assessment-count">{assessments.length} assessments</span></div>
        <div className="overview-grid"><article className="chart-card"><div className="card-heading"><div><h3>Latest assessment by domain</h3><p>Score and result status from the API</p></div></div><div className="domain-list">{typeOptions.map(({ id, label, icon: Icon }) => { const detail = details[id]; return <div className="domain-row" key={id}><span className="domain-icon"><Icon size={16} /></span><span className="domain-name">{label}</span><div className="domain-track"><i style={{ width: `${detail?.score ?? 0}%` }} /></div><b>{detail?.score ?? "—"}</b><Link href={detail ? `/assessments/${detail.id}` : "#"} aria-label={`Open ${label} assessment`} onClick={(event) => { if (!detail) { event.preventDefault(); setAssessmentType(id); setModalOpen(true); } }}><ArrowRight size={15} /></Link></div>; })}</div><div className="chart-foot"><span><b>{assessments.length}</b> total assessments</span><span>{loading ? "Refreshing" : connectionError ? "API offline" : "Live data"}</span></div></article>
          <article className="distribution-card"><div className="card-heading"><div><h3>Assessment pipeline</h3><p>Current persisted assessment status</p></div></div><div className="pipeline-summary"><div><b>{assessments.length}</b><span>Total assessments</span></div><div><b>{statusCounts.Completed ?? 0}</b><span>Completed</span></div></div><div className="pipeline-list">{(["Running", "Pending", "Failed"] as Status[]).map((status) => <div key={status}><span className={`pipeline-dot ${status.toLowerCase()}`} />{status}<b>{statusCounts[status] ?? 0}</b></div>)}</div><div className="landscape-note"><span className="note-spark"><LockKeyhole size={14} /></span><span>Results are read from your configured assessment API.</span></div></article></div>

        <div className="section-heading phase3-heading" id="AIInsights"><div><h2>AI Insights</h2><p>Executive context generated from deterministic assessment facts</p></div></div>
        <div className="ai-insights-grid">{typeOptions.map(({ id, label }) => { const detail = details[id]; const insight = detail?.insights?.[0]; return <article className="ai-insight-card" key={id}><div className="ai-insight-header"><div><span className="insight-overline">{label}</span><h3>{insight?.status === "Completed" ? insight.recommendedStrategy || detail?.recommendation : "Insight summary"}</h3></div><span className={`ai-status ${insight?.status?.toLowerCase() ?? "waiting"}`}>{insight?.status ?? (detail ? "Not generated" : "No assessment")}</span></div>{insight?.status === "Completed" ? <><p className="ai-executive-summary">{insight.executiveSummary}</p><div className="ai-impact"><b>Business impact</b><span>{insight.businessImpact}</span></div><div className="ai-top-recs"><b>AI recommendations</b>{insight.recommendations.slice(0, 3).map((item) => <div key={`${item.priority}-${item.title}`}><i>{item.priority}</i><span>{item.title}</span></div>)}</div><div className="ai-insight-footer"><span className={`confidence-badge ${insight.confidence?.toLowerCase() ?? "low"}`}>{insight.confidence ?? "Low"} confidence</span><span>{insight.modelName} · {insight.tokenUsage ?? 0} tokens</span><Link href={`/assessments/${detail!.id}`}>Open insight <ArrowRight size={13} /></Link></div><button className="regenerate-link" onClick={() => void regenerateInsight(detail!)}>Regenerate insight</button></> : <div className="ai-empty">{insight?.status === "Pending" || insight?.status === "Running" ? "Generating the executive summary and roadmap…" : insight?.errorMessage ?? (detail ? "AI insights are generated after deterministic results are saved. Configure a provider, then generate an insight." : "Complete an assessment to create its evidence-based AI insight.")}{detail && insight?.status !== "Pending" && insight?.status !== "Running" && <button onClick={() => void regenerateInsight(detail)}>Generate insight</button>}</div>}</article>; })}</div>

        <div className="section-heading phase3-heading"><div><h2>Architecture health trends</h2><p>Historical scores and finding movement from completed assessments</p></div></div>
        <div className="trends-grid">{trends.map((trend) => <article className="trend-card" key={trend.id}><div className="trend-card-header"><div><span>{trend.label}</span><b>{trend.last?.score ?? "—"}<small>{trend.last ? "/100" : "No history"}</small></b></div><TrendSpark values={trend.history.map((item) => item.score ?? 0)} /></div><div className="trend-stats"><span>{trend.history.length} completed runs</span>{trend.change === null ? <span>Need 2 runs to show movement</span> : <span className={trend.change >= 0 ? "trend-positive" : "trend-negative"}>{trend.change >= 0 ? <ArrowUpRight size={13} /> : <ArrowDownRight size={13} />}{trend.change > 0 ? "+" : ""}{trend.change} points{trend.improvementPercent !== null ? ` · ${trend.improvementPercent > 0 ? "+" : ""}${trend.improvementPercent.toFixed(1)}%` : ""}</span>}</div><div className="risk-movement-note">{trend.findingChange === null ? "Risk movement appears after a second completed assessment." : `${Math.abs(trend.findingChange)} ${trend.findingChange >= 0 ? "fewer" : "more"} findings than baseline`}</div></article>)}</div>

        <div className="section-heading phase3-heading"><div><h2>Assessment comparison</h2><p>Deterministic score and finding changes, followed by an optional AI explanation</p></div></div>
        <article className="comparison-card"><div className="comparison-controls"><label>Baseline<select value={baselineId} onChange={(event) => setBaselineId(event.target.value)}><option value="">Choose baseline</option>{completedAssessments.map((item) => <option key={item.id} value={item.id}>{item.name} · {typeLabel[item.assessmentType]} · {item.score}</option>)}</select></label><ArrowRight size={16} /><label>Comparison<select value={comparisonId} onChange={(event) => setComparisonId(event.target.value)}><option value="">Choose assessment</option>{completedAssessments.map((item) => <option key={item.id} value={item.id}>{item.name} · {typeLabel[item.assessmentType]} · {item.score}</option>)}</select></label><button className="button primary" onClick={() => void runComparison()} disabled={comparing || completedAssessments.length < 2}>{comparing ? "Comparing…" : "Compare"}</button></div>{compareError && <div className="form-error">{compareError}</div>}{comparison && <div className="comparison-results"><div className="comparison-score"><span>Score change</span><strong className={comparison.scoreChange >= 0 ? "trend-positive" : "trend-negative"}>{comparison.scoreChange > 0 ? "+" : ""}{comparison.scoreChange}</strong><small>{comparison.baselineScore} → {comparison.comparisonScore} · {comparison.scoreDirection}</small></div><div className="comparison-finding-stats"><span><b>{comparison.resolvedFindings.length}</b> resolved</span><span><b>{comparison.newFindings.length}</b> new</span><span><b>{comparison.improvementAreas.length}</b> areas improved</span><span><b>{comparison.regressionAreas.length}</b> regression areas</span></div><div className="comparison-lists"><div><b>Resolved findings</b>{comparison.resolvedFindings.length ? comparison.resolvedFindings.map((item) => <span key={`${item.category}-${item.title}`}>{item.title} · {item.severity}</span>) : <small>None</small>}</div><div><b>New findings</b>{comparison.newFindings.length ? comparison.newFindings.map((item) => <span key={`${item.category}-${item.title}`}>{item.title} · {item.severity}</span>) : <small>None</small>}</div></div>{comparison.aiExplanation && <div className="comparison-explanation"><b>AI comparison explanation</b><p>{comparison.aiExplanation}</p></div>}{comparison.aiExplanationError && <div className="ai-empty">Deterministic comparison is available; AI explanation failed: {comparison.aiExplanationError}</div>}</div>}</article>

        <div className="lower-grid"><article className="findings-card"><div className="card-heading"><div><h3>Priority findings</h3><p>Findings generated from your uploaded artifacts</p></div></div>{findings.length ? <div className="finding-list">{findings.slice(0, 7).map((finding, index) => <div className="finding-row" key={`${finding.title}-${index}`}><span className={`severity ${severityClass[finding.severity]}`}>{finding.severity}</span><div className="finding-copy"><b>{finding.title}</b><span>{finding.description}</span></div><span className="finding-area">{finding.category}</span></div>)}</div> : <div className="empty-panel">{loading ? "Loading findings…" : "No persisted findings yet. Run an assessment after adding JSON artifacts."}</div>}<div className="all-findings-note">{findings.length} findings across completed assessments</div></article>
          <article className="roadmap-card"><div className="card-heading"><div><h3>Recommended next steps</h3><p>Ordered by assessment priority</p></div></div>{recommendations.length ? <div className="roadmap-list">{recommendations.slice(0, 6).map((item) => <div className="roadmap-step" key={`${item.priority}-${item.title}`}><div className="roadmap-marker blue"><span /></div><div className="roadmap-body"><div className="roadmap-period">Priority {item.priority} · {item.timeframe}</div><b>{item.title}</b><p>{item.description}</p></div></div>)}</div> : <div className="empty-panel">Recommendations appear after an assessment completes.</div>}</article></div>

        <div className="section-heading history-heading"><div><h2>Recent assessments</h2><p>Assessment runs persisted by the API</p></div></div><div className="history-card">{assessments.length ? assessments.slice(0, 8).map((item) => <Link className="history-row" href={`/assessments/${item.id}`} key={item.id}><span className={`history-status ${item.status.toLowerCase()}`} /><span className="history-name"><b>{item.name}</b><small>{item.organizationName} · {typeLabel[item.assessmentType]}</small></span><span className="history-date">{new Date(item.createdDate).toLocaleDateString()}</span><span className="history-result">{item.status === "Completed" ? `${item.score ?? "—"}/100` : item.status}</span><ArrowRight size={15} /></Link>) : <div className="empty-panel">{loading ? "Loading assessments…" : "No assessment records are available."}</div>}</div>
        <div className="last-updated"><span>Deterministic findings remain authoritative · AI output is supplementary</span><span><LockKeyhole size={12} /> Stored in your configured database</span></div>
      </div>
    </section>

    {modalOpen && <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) setModalOpen(false); }}><section className="assessment-modal" role="dialog" aria-modal="true" aria-labelledby="modal-title"><div className="modal-top"><div className="modal-icon"><Sparkles size={19} /></div><button className="modal-close" onClick={() => setModalOpen(false)} aria-label="Close" disabled={submitting}><X size={18} /></button></div><div className="modal-kicker">NEW ASSESSMENT</div><h2 id="modal-title">Assess your software architecture</h2><p className="modal-description">Choose one deterministic assessment type and attach JSON inventory files. Results will be calculated by local rules.</p><label className="field-label" htmlFor="assessment-name">Assessment name</label><input className="text-input" id="assessment-name" value={name} onChange={(event) => setName(event.target.value)} maxLength={120} /><label className="field-label organization-label" htmlFor="organization-name">Organization</label><input className="text-input" id="organization-name" value={organization} onChange={(event) => setOrganization(event.target.value)} maxLength={120} /><label className="field-label module-label">Assessment type</label><div className="module-options">{typeOptions.map(({ id, label, description, icon: Icon }) => <label key={id}><input type="radio" name="assessment-type" checked={assessmentType === id} onChange={() => setAssessmentType(id)} /><span className="check-box"><Check size={13} /></span><Icon size={17} /><span><b>{label}</b><small>{description}</small></span></label>)}</div><label className="field-label upload-label">JSON artifacts <span>Required · 2 MB max each</span></label><button className="dropzone" onClick={() => inputRef.current?.click()} onDragOver={(event) => event.preventDefault()} onDrop={(event) => { event.preventDefault(); addFiles(event.dataTransfer.files); }}><span className="upload-icon"><CloudUpload size={19} /></span><span><b>Drop JSON files here or <em>browse</em></b><small>Only valid .json files are accepted</small></span></button><div className="sample-json-link">Need a starting point? <a href={`/samples/${assessmentType === "Modernization" ? "modernization" : assessmentType === "DataMaturity" ? "data-maturity" : "service-consistency"}.json`} download>Download example JSON</a></div><input ref={inputRef} type="file" multiple accept="application/json,.json" hidden onChange={(event) => addFiles(event.target.files)} />{files.length > 0 && <div className="file-list">{files.map(({ file, id }) => <div className="file-row" key={id}><Database size={15} /><span>{file.name}</span><small>{(file.size / 1024).toFixed(0)} KB</small><button onClick={() => setFiles((all) => all.filter((item) => item.id !== id))} aria-label={`Remove ${file.name}`}><X size={14} /></button></div>)}</div>}{uploadError && <div className="form-error">{uploadError}</div>}<div className="modal-privacy"><LockKeyhole size={13} />Files are stored under the backend uploads folder.</div><div className="modal-actions"><button className="button secondary" onClick={() => setModalOpen(false)} disabled={submitting}>Cancel</button><button className="button primary" onClick={() => void startAssessment()} disabled={submitting || !!connectionError}>{submitting ? "Uploading & starting…" : <><Sparkles size={15} />Run assessment <ArrowRight size={15} /></>}</button></div></section></div>}
    <button className="mobile-menu" aria-label={menuOpen ? "Close menu" : "Open menu"} aria-expanded={menuOpen} onClick={() => setMenuOpen((open) => !open)}>{menuOpen ? <X size={20} /> : <Menu size={20} />}</button>
  </main>;
}
