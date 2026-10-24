"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, ArrowRight, CheckCircle2, CircleAlert, Clock3, Download, FileJson, LoaderCircle, ShieldAlert } from "lucide-react";

type Severity = "Critical" | "High" | "Medium" | "Low";
type Assessment = { id: string; name: string; organizationName: string; assessmentType: string; status: string; createdDate: string; completedDate: string | null; score: number | null; recommendation: string | null; executiveSummary: string | null; applicationCount: number; errorMessage: string | null; scoreCards: { dimension: string; score: number }[]; findings: { title: string; description: string; category: string; severity: Severity; recommendation: string }[]; recommendations: { priority: number; title: string; description: string; timeframe: string }[]; artifactNames: string[] };
const apiBase = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
const groups: Severity[] = ["Critical", "High", "Medium", "Low"];

export default function AssessmentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [data, setData] = useState<Assessment | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let alive = true;
    const load = async () => {
      try {
        const response = await fetch(`${apiBase}/api/assessments/${id}`, { cache: "no-store" });
        if (!response.ok) throw new Error(response.status === 404 ? "Assessment not found." : "Could not load this assessment.");
        if (alive) { setData(await response.json()); setError(""); }
      } catch (e) { if (alive) setError(e instanceof Error ? e.message : "Assessment API is unavailable."); }
    };
    void load();
    const timer = window.setInterval(() => { if (data?.status === "Pending" || data?.status === "Running") void load(); }, 1600);
    return () => { alive = false; window.clearInterval(timer); };
  }, [id, data?.status]);

  if (!data && !error) return <main className="detail-loading"><LoaderCircle size={22} className="spin" />Loading assessment results…</main>;
  if (error || !data) return <main className="detail-page"><Link href="/" className="back-link"><ArrowLeft size={15} />Dashboard</Link><div className="api-alert"><ShieldAlert size={18} /><div><b>Unable to open assessment</b><span>{error}</span></div></div></main>;

  const formatDate = (date: string | null) => date ? new Date(date).toLocaleString() : "—";
  const statusClass = data.status.toLowerCase();
  return <main className="detail-page"><div className="detail-topline"><Link href="/" className="back-link"><ArrowLeft size={15} />Back to dashboard</Link><button className="button secondary" onClick={() => window.print()}><Download size={14} />Print / save PDF</button></div><div className="detail-heading"><div><div className="eyebrow"><span className="live-dot" />ASSESSMENT RESULTS</div><h1>{data.name}</h1><p>{data.organizationName} <span>·</span> {data.assessmentType.replace(/([A-Z])/g, " $1").trim()} <span>·</span> Created {formatDate(data.createdDate)}</p></div><span className={`detail-status ${statusClass}`}>{data.status === "Completed" ? <CheckCircle2 size={14} /> : data.status === "Failed" ? <CircleAlert size={14} /> : <Clock3 size={14} />}{data.status}</span></div>
    {data.status === "Pending" || data.status === "Running" ? <div className="running-banner"><LoaderCircle size={17} className="spin" /><div><b>Assessment is running</b><span>Your JSON artifacts are being evaluated by deterministic rules. This page refreshes automatically.</span></div></div> : null}
    {data.status === "Failed" && <div className="api-alert"><CircleAlert size={18} /><div><b>Assessment failed</b><span>{data.errorMessage || "The uploaded data did not match the assessment schema."}</span></div></div>}
    {data.status === "Completed" && <><section className="result-summary"><div className="result-score"><span>Overall score</span><strong>{data.score}<small> / 100</small></strong><em>{data.recommendation}</em></div><div className="summary-copy"><h2>Executive summary</h2><p>{data.executiveSummary}</p><div className="summary-meta"><span>{data.assessmentType === "DataMaturity" ? `${data.scoreCards.length} dimensions scored` : `${data.applicationCount} systems assessed`}</span><span>{data.findings.length} findings</span><span>Completed {formatDate(data.completedDate)}</span></div></div></section>
      {data.scoreCards.length > 1 && <section className="detail-section"><div className="section-heading"><div><h2>Scorecard</h2><p>Scores calculated from reported evidence</p></div></div><div className="scorecard-grid">{data.scoreCards.map((card) => <div className="scorecard-item" key={card.dimension}><div><b>{card.dimension}</b><strong>{card.score}</strong></div><div className="domain-track"><i style={{ width: `${card.score}%` }} /></div></div>)}</div></section>}
      <section className="detail-section"><div className="section-heading"><div><h2>Findings</h2><p>{data.findings.length} rule-based findings grouped by severity</p></div></div>{data.findings.length ? <div className="severity-groups">{groups.map((group) => { const findings = data.findings.filter((item) => item.severity === group); return findings.length ? <section className="severity-group" key={group}><h3><span className={`severity ${group.toLowerCase()}`}>{group}</span>{findings.length} {findings.length === 1 ? "finding" : "findings"}</h3>{findings.map((finding, index) => <article className="detail-finding" key={`${finding.title}-${index}`}><div><b>{finding.title}</b><span>{finding.category}</span></div><p>{finding.description}</p><div className="finding-recommendation"><b>Suggested action</b><span>{finding.recommendation}</span></div></article>)}</section> : null; })}</div> : <div className="empty-panel">No inconsistencies or rule violations were found in the uploaded evidence.</div>}</section>
      <section className="detail-section"><div className="section-heading"><div><h2>Recommendations & roadmap</h2><p>Prioritized actions from the assessment rules</p></div></div>{data.recommendations.length ? <div className="detail-roadmap">{data.recommendations.map((rec) => <article className="detail-recommendation" key={`${rec.priority}-${rec.title}`}><div className="recommendation-priority">{rec.priority}</div><div><span>{rec.timeframe}</span><b>{rec.title}</b><p>{rec.description}</p></div><ArrowRight size={15} /></article>)}</div> : <div className="empty-panel">No additional actions were generated.</div>}</section></>}
    <section className="detail-section artifact-section"><div className="section-heading"><div><h2>Uploaded artifacts</h2><p>JSON files attached to this assessment</p></div></div><div className="artifact-list">{data.artifactNames.length ? data.artifactNames.map((file, i) => <div className="artifact-row" key={`${file}-${i}`}><FileJson size={16} /><span>{file}</span></div>) : <div className="empty-panel">No artifacts are attached.</div>}</div></section>
  </main>;
}
