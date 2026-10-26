"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, ArrowRight, GitBranch, Play, Plus, Sparkles, Target, Workflow } from "lucide-react";

const api = (process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5080").replace(/\/$/, "");
type Assessment = { id: string; name: string; assessmentType: string; status: string; score: number | null };
type App = { id: string; name: string; technicalRisk: number; businessImpact: number };
type Repository = { id: string; name: string; applications: App[]; services: { name: string }[]; assessments?: { id: string }[] };
type ChangeType = "RuntimeUpgrade" | "DatabaseUpgrade" | "Containerize" | "MoveToCloud" | "StandardizeAuthentication" | "StandardizeErrorHandling" | "SplitMonolith" | "RemoveLegacyDependency";
type Scenario = { id: string; assessmentId: string; name: string; status: string; advisorExplanation: string | null; changes: { changeType: ChangeType; target: string; oldValue: string; newValue: string }[]; result: { baselineScore: number; projectedScore: number; riskReduction: number; cloudReadinessChange: number; technicalDebtReduction: number; priorityScore: number; effortLevel: string; riskLevel: string; recommendation: string; summary: string } | null };
type Opportunity = { applicationId: string; applicationName: string; repositoryName: string; technicalDebt: number; businessImpact: number; dependencyComplexity: number; securityRisk: number; priorityScore: number; priorityBand: string; currentScore: number; projectedScore: number; effortLevel: string; recommendation: string };
type Edge = { id: string; source: string; target: string; dependencyType: string };
type ChangeInput = { changeType: ChangeType; target: string; oldValue: string; newValue: string };
const choices: { type: ChangeType; label: string; old: string; next: string }[] = [
  { type: "RuntimeUpgrade", label: "Upgrade runtime", old: ".NET Framework / legacy runtime", next: ".NET 9 / supported runtime" },
  { type: "DatabaseUpgrade", label: "Upgrade database", old: "SQL Server 2012 / legacy", next: "SQL Server 2022" },
  { type: "Containerize", label: "Containerize", old: "VM or unknown hosting", next: "Docker / OCI container" },
  { type: "MoveToCloud", label: "Move to cloud", old: "On-premises / unknown", next: "Managed cloud platform" },
  { type: "StandardizeAuthentication", label: "Standardize authentication", old: "Mixed or missing", next: "OAuth 2.0 / OIDC" },
  { type: "StandardizeErrorHandling", label: "Standardize error handling", old: "Application-specific", next: "Shared error contract" },
  { type: "SplitMonolith", label: "Split monolith", old: "Monolithic application", next: "Service-oriented architecture" },
  { type: "RemoveLegacyDependency", label: "Remove legacy dependency", old: "Unsupported dependency", next: "Supported alternative" },
];
async function json<T>(url: string, init?: RequestInit): Promise<T> { const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } }); if (!response.ok) { let message = response.statusText; try { const body = await response.json(); message = body.error || body.detail || body.title || message; } catch { /* response has no JSON body */ } throw new Error(message); } return response.json(); }

export default function SimulatorPage() {
  const [assessments, setAssessments] = useState<Assessment[]>([]);
  const [repositories, setRepositories] = useState<Repository[]>([]);
  const [assessmentId, setAssessmentId] = useState("");
  const [repositoryId, setRepositoryId] = useState("");
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [name, setName] = useState("Modernize legacy runtime");
  const [description, setDescription] = useState("");
  const [changes, setChanges] = useState<ChangeInput[]>([]);
  const [target, setTarget] = useState("Portfolio");
  const [changeType, setChangeType] = useState<ChangeType>("RuntimeUpgrade");
  const [opportunities, setOpportunities] = useState<Opportunity[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [source, setSource] = useState("");
  const [targetSystem, setTargetSystem] = useState("");
  const [impact, setImpact] = useState<{ selectedSystem: string; directDependencies: string[]; downstreamSystems: string[]; affectedApplications: string[]; blastRadius: number; isSinglePointOfFailure: boolean } | null>(null);
  const [roadmap, setRoadmap] = useState<{ sequence: number; period: string; applicationName: string; priorityScore: number; effortLevel: string; recommendation: string }[]>([]);
  const [advisor, setAdvisor] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const selectedRepository = repositories.find((x) => x.id === repositoryId);
  const assessmentRepository = repositories.find((x) => x.assessments?.some((a) => a.id === assessmentId));
  const appNames = selectedRepository?.applications.map((x) => x.name) ?? [];
  const systems = [...appNames, ...(selectedRepository?.services.map((x) => x.name) ?? [])];
  const loadScenarios = useCallback(async (id: string) => { if (!id) { setScenarios([]); return; } try { setScenarios(await json<Scenario[]>(`${api}/api/assessments/${id}/scenarios`)); } catch (e) { setError(e instanceof Error ? e.message : "Unable to load scenarios."); } }, []);
  const loadRepo = useCallback(async (id: string) => {
    if (!id) return;
    try {
      const repo = await json<Repository>(`${api}/api/repositories/${id}`);
      setRepositories((old) => old.map((x) => x.id === id ? { ...x, applications: repo.applications, services: repo.services } : x));
      const repositorySystems = [...repo.applications.map((x) => x.name), ...repo.services.map((x) => x.name)];
      const [ranked, graph, plan] = await Promise.all([
        json<Opportunity[]>(`${api}/api/repositories/${id}/portfolio`),
        json<Edge[]>(`${api}/api/repositories/${id}/dependencies`),
        json<typeof roadmap>(`${api}/api/repositories/${id}/roadmap`),
      ]);
      setOpportunities(ranked); setEdges(graph); setRoadmap(plan);
      setSource(repositorySystems[0] ?? ""); setTargetSystem(repositorySystems[1] ?? "");
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load portfolio."); }
  }, []);

  useEffect(() => { void (async () => { try {
    const [a, r] = await Promise.all([json<Assessment[]>(`${api}/api/assessments`), json<Repository[]>(`${api}/api/inventory`)]);
    setAssessments(a.filter((x) => x.status === "Completed" && x.score !== null)); setRepositories(r);
    if (a.some((x) => x.status === "Completed" && x.score !== null)) setAssessmentId(a.find((x) => x.assessmentType === "Modernization" && x.status === "Completed")?.id ?? a.find((x) => x.status === "Completed")!.id);
    if (r.length) setRepositoryId(r[0].id);
  } catch (e) { setError(e instanceof Error ? e.message : "Unable to connect to the API."); } })(); }, []);
  useEffect(() => { void loadScenarios(assessmentId); }, [assessmentId, loadScenarios]);
  useEffect(() => { void loadRepo(repositoryId); }, [repositoryId, loadRepo]);

  const addChange = () => { const choice = choices.find((x) => x.type === changeType)!; setChanges((old) => [...old, { changeType, target, oldValue: choice.old, newValue: choice.next }]); };
  const createAndRun = async () => {
    if (!assessmentId || !changes.length) { setError("Choose a completed assessment and add at least one change."); return; }
    setBusy(true); setError(""); setNotice("");
    try {
      const created = await json<Scenario>(`${api}/api/assessments/${assessmentId}/scenarios`, { method: "POST", body: JSON.stringify({ name, description }) });
      for (const change of changes) await json(`${api}/api/scenarios/${created.id}/changes`, { method: "POST", body: JSON.stringify(change) });
      await json(`${api}/api/scenarios/${created.id}/simulate`, { method: "POST" });
      await loadScenarios(assessmentId); setChanges([]); setNotice("Scenario simulated. The assessment baseline remains unchanged.");
    } catch (e) { setError(e instanceof Error ? e.message : "Simulation failed."); }
    finally { setBusy(false); }
  };
  const addDependency = async () => { if (!repositoryId || !source || !targetSystem || source === targetSystem) return; setError(""); try { await json(`${api}/api/repositories/${repositoryId}/dependencies`, { method: "POST", body: JSON.stringify({ source, target: targetSystem, dependencyType: "API" }) }); await loadRepo(repositoryId); setNotice("Dependency link saved to the repository inventory."); } catch (e) { setError(e instanceof Error ? e.message : "Unable to save dependency."); } };
  const inspectImpact = async (selected: string) => { if (!repositoryId || !selected) return; setError(""); try { setImpact(await json(`${api}/api/repositories/${repositoryId}/impact/${encodeURIComponent(selected)}`)); } catch (e) { setError(e instanceof Error ? e.message : "Unable to calculate impact."); } };
  const askAdvisor = async (scenario: Scenario) => { setBusy(true); setError(""); try { const result = await json<{ explanation: string }>(`${api}/api/scenarios/${scenario.id}/advisor`, { method: "POST" }); setAdvisor((old) => ({ ...old, [scenario.id]: result.explanation })); } catch (e) { setError(e instanceof Error ? e.message : "AI scenario advice is unavailable."); } finally { setBusy(false); } };

  return <div className="sim-page">
    <header className="sim-top"><Link href="/" className="sim-back"><ArrowLeft size={15} /> Overview</Link><span>ARCHITECTURE PLANNING</span></header>
    <main className="sim-main">
      <div className="sim-heading"><div><div className="eyebrow"><span className="live-dot" />TRANSFORMATION PLANNING</div><h1>Transformation simulator</h1><p>Explore modernization options against an immutable assessment baseline, then prioritize portfolio investments.</p></div><Link className="button secondary" href="/"><ArrowRight size={14} /> Return to overview</Link></div>
      {error && <div className="sim-alert">{error}<button onClick={() => setError("")}>Dismiss</button></div>}{notice && <div className="sim-notice">{notice}</div>}
      <section className="sim-grid">
        <article className="sim-card sim-builder"><div className="sim-card-head"><div className="sim-icon"><Workflow size={17} /></div><div><h2>Build a scenario</h2><p>Compare deterministic projections with a completed assessment.</p></div></div>
          <label>Assessment baseline<select value={assessmentId} onChange={(e) => setAssessmentId(e.target.value)}><option value="">Select completed assessment</option>{assessments.map((x) => <option key={x.id} value={x.id}>{x.name} · {x.score}/100</option>)}</select></label>
          <label>Scenario name<input value={name} onChange={(e) => setName(e.target.value)} maxLength={160} /></label>
          <label>Description<input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} placeholder="Optional planning context" /></label>
          <div className="sim-add-row"><label>Transformation<select value={changeType} onChange={(e) => setChangeType(e.target.value as ChangeType)}>{choices.map((x) => <option key={x.type} value={x.type}>{x.label}</option>)}</select></label><label>Target<select value={target} onChange={(e) => setTarget(e.target.value)}><option value="Portfolio">Entire assessment</option>{(assessmentRepository?.applications ?? []).map((x) => <option key={x.id} value={x.name}>{x.name}</option>)}</select></label><button className="button secondary sim-add" onClick={addChange}><Plus size={14} /> Add</button></div>
          {changes.length > 0 && <div className="sim-change-list">{changes.map((x, i) => <div key={`${x.changeType}-${i}`}><b>{choices.find((y) => y.type === x.changeType)?.label}</b><span>{x.target}: {x.oldValue} → {x.newValue}</span><button onClick={() => setChanges((old) => old.filter((_, j) => i !== j))}>Remove</button></div>)}</div>}
          <button className="button primary sim-run" onClick={() => void createAndRun()} disabled={busy || changes.length === 0 || !assessmentId}><Play size={14} />{busy ? "Working…" : "Run simulation"}</button>
        </article>
        <article className="sim-card sim-baseline"><div className="sim-card-head"><div className="sim-icon green"><Target size={17} /></div><div><h2>Portfolio prioritization</h2><p>35% technical debt · 30% business impact · 20% dependency complexity · 15% security risk</p></div></div>
          <label>Repository<select value={repositoryId} onChange={(e) => setRepositoryId(e.target.value)}><option value="">Select repository</option>{repositories.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          {opportunities.length ? <div className="sim-rank-list">{opportunities.map((x, i) => <div className="sim-rank" key={x.applicationId}><strong>{i + 1}</strong><div><b>{x.applicationName}</b><span>{x.repositoryName} · {x.priorityBand} · {x.effortLevel} effort</span></div><em>{x.priorityScore}</em></div>)}</div> : <div className="sim-empty">Upload a repository with recognized application manifests to rank its systems.</div>}
        </article>
      </section>

      <section className="sim-card sim-outcomes"><div className="sim-card-head"><div className="sim-icon purple"><Sparkles size={17} /></div><div><h2>Scenario outcomes</h2><p>Baseline and projected scores. Each scenario is stored independently from its assessment.</p></div><span className="sim-count">{scenarios.length} scenarios</span></div>
        {!scenarios.length ? <div className="sim-empty">No scenarios for this assessment yet. Add a transformation above to create the first projection.</div> : <div className="sim-scenario-grid">{scenarios.map((s) => <article className="sim-scenario" key={s.id}><div className="sim-scenario-title"><div><span>{s.status}</span><h3>{s.name}</h3></div><b>{s.result?.priorityScore ?? "—"}<small>priority</small></b></div><div className="sim-score-pair"><div><span>Current</span><strong>{s.result?.baselineScore ?? "—"}</strong></div><ArrowRight size={16} /><div><span>Projected</span><strong className="sim-projected">{s.result?.projectedScore ?? "—"}</strong></div><em>+{s.result?.riskReduction ?? 0} pts</em></div><div className="sim-outcome-metrics"><span>Cloud readiness <b>{(s.result?.cloudReadinessChange ?? 0) > 0 ? "+" : ""}{s.result?.cloudReadinessChange ?? 0}</b></span><span>Effort <b>{s.result?.effortLevel ?? "—"}</b></span><span>Risk <b>{s.result?.riskLevel ?? "—"}</b></span></div><p>{s.result?.summary ?? "Draft scenario; run simulation to calculate outcomes."}</p><div className="sim-change-chips">{s.changes.map((c, i) => <span key={i}>{choices.find((x) => x.type === c.changeType)?.label} · {c.target}</span>)}</div>{s.result && <button className="sim-advisor" onClick={() => void askAdvisor(s)} disabled={busy}><Sparkles size={13} /> Ask AI scenario advisor</button>}{(advisor[s.id] || s.advisorExplanation) && <blockquote className="sim-advisor-copy">{advisor[s.id] || s.advisorExplanation}</blockquote>}</article>)}</div>}
      </section>

      <section className="sim-grid sim-lower-grid">
        <article className="sim-card"><div className="sim-card-head"><div className="sim-icon"><GitBranch size={17} /></div><div><h2>Dependency graph & blast radius</h2><p>Record known system links, then trace downstream impacts.</p></div></div>
          <div className="sim-dependency-form"><label>Source<select value={source} onChange={(e) => setSource(e.target.value)}>{systems.map((x) => <option key={x}>{x}</option>)}</select></label><label>Depends on<select value={targetSystem} onChange={(e) => setTargetSystem(e.target.value)}>{systems.map((x) => <option key={x}>{x}</option>)}</select></label><button className="button secondary" onClick={() => void addDependency()} disabled={systems.length < 2}>Record link</button></div>
          <div className="sim-edge-list">{edges.map((x) => <div key={x.id}><span>{x.source}</span><ArrowRight size={13} /><b>{x.dependencyType}</b><ArrowRight size={13} /><span>{x.target}</span></div>)}</div>
          <label>Inspect system impact<select onChange={(e) => void inspectImpact(e.target.value)} defaultValue=""><option value="">Choose a system</option>{systems.map((x) => <option key={x}>{x}</option>)}</select></label>
          {impact && <div className="sim-impact"><b>{impact.selectedSystem}</b><span>{impact.blastRadius} affected system(s) · {impact.isSinglePointOfFailure ? "Potential single point of failure" : "No single point of failure flagged"}</span><div><small>Downstream: {impact.downstreamSystems.join(" → ") || "No downstream systems recorded"}</small></div><div><small>Direct dependencies: {impact.directDependencies.join(", ") || "None recorded"}</small></div></div>}
        </article>
        <article className="sim-card"><div className="sim-card-head"><div className="sim-icon orange"><Target size={17} /></div><div><h2>Executive investment roadmap</h2><p>Priority and relative effort shape the suggested sequence.</p></div></div>
          {roadmap.length ? <div className="sim-roadmap">{roadmap.map((x) => <div key={x.sequence}><span>{x.period}</span><b>{x.applicationName}</b><small>Priority {x.priorityScore} · {x.effortLevel} effort · {x.recommendation}</small></div>)}</div> : <div className="sim-empty">A roadmap appears after repository applications are discovered.</div>}
        </article>
      </section>
      <p className="sim-disclaimer">Projections use transparent demo rules and available repository evidence. They are relative planning signals, not delivery estimates, financial ROI, security attestations, or changes to assessment results. Dependency links are recorded explicitly by the architecture team.</p>
    </main>
  </div>;
}
