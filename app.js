const findings = [
  { severity:"Critical", title:"Human approval authority is not explicitly assigned", section:"§4.2", text:"The framework requires approval but does not name the accountable executive role.", status:"Needs evidence" },
  { severity:"Critical", title:"Production rollback criteria are incomplete", section:"§7.5", text:"Rollback triggers lack measurable thresholds and an accountable owner.", status:"Open" },
  { severity:"Critical", title:"Evidence retention period conflicts with privacy policy", section:"§8.1", text:"Proposed evidence retention exceeds the current data-retention limit.", status:"Needs evidence" },
  { severity:"High", title:"Model risk register lacks update cadence", section:"§3.4", text:"The register requires ownership but no review frequency is specified.", status:"Open" },
  { severity:"High", title:"Monitoring UAT acceptance criteria are ambiguous", section:"§7.8", text:"No quantified service-level thresholds are defined for deployment approval.", status:"Open" },
  { severity:"High", title:"Vendor controls are not mapped to internal controls", section:"§6.3", text:"Third-party requirements are listed but not traceable to internal owners.", status:"Needs evidence" },
  { severity:"High", title:"High-risk decision boundaries are inconsistent", section:"§4.6", text:"The scope of mandatory human validation differs across sections.", status:"Open" },
  { severity:"High", title:"Incident escalation deadlines are undefined", section:"§9.2", text:"Response and executive notification timeframes are not stated.", status:"Open" },
  { severity:"Medium", title:"Terminology should be normalized", section:"§2.1", text:"The terms model owner and system owner are used interchangeably.", status:"Resolved" },
  { severity:"Medium", title:"Date formats vary across control templates", section:"Appendix A", text:"Templates mix ISO and regional date formats.", status:"Open" },
  { severity:"Medium", title:"Training records do not include re-certification", section:"§5.3", text:"Annual re-certification is implied but not required.", status:"Open" },
  { severity:"Medium", title:"Audit export fields omit reviewer identity", section:"§10.2", text:"The export schema excludes the reviewer who approved each finding.", status:"Open" }
];

const actions = [
  { title:"Define human approval authority", owner:"Governance Council", due:"Nov 28, 2026", status:"Pending" },
  { title:"Publish model risk register", owner:"Risk & Compliance", due:"Dec 02, 2026", status:"Pending" },
  { title:"Complete production monitoring UAT", owner:"Platform Team", due:"Dec 05, 2026", status:"In progress" },
  { title:"Resolve evidence retention conflict", owner:"Privacy Office", due:"Dec 08, 2026", status:"Pending" },
  { title:"Define rollback thresholds", owner:"SRE Team", due:"Dec 10, 2026", status:"Pending" },
  { title:"Map vendor controls", owner:"Third-party Risk", due:"Dec 12, 2026", status:"Pending" },
  { title:"Clarify high-risk boundaries", owner:"Legal", due:"Dec 15, 2026", status:"In progress" },
  { title:"Standardize audit export fields", owner:"Data Platform", due:"Dec 18, 2026", status:"Pending" }
];

function switchTab(name) {
  document.querySelectorAll(".tab").forEach(tab => tab.classList.toggle("active", tab.dataset.tab === name));
  document.querySelectorAll(".tab-panel").forEach(panel => panel.classList.toggle("active", panel.id === name));
  window.scrollTo({ top: 0, behavior:"smooth" });
}

document.querySelectorAll(".tab").forEach(tab => tab.addEventListener("click", () => switchTab(tab.dataset.tab)));
document.querySelectorAll("[data-switch]").forEach(button => button.addEventListener("click", () => switchTab(button.dataset.switch)));
document.querySelectorAll(".nav-item").forEach(item => item.addEventListener("click", () => {
  document.querySelectorAll(".nav-item").forEach(nav => nav.classList.remove("active"));
  item.classList.add("active");
  if (item.dataset.view === "history") switchTab("findings");
  else switchTab("digest");
}));

function renderFindings() {
  const query = document.getElementById("findingSearch")?.value?.toLowerCase() || "";
  const severity = document.getElementById("severityFilter").value;
  const list = document.getElementById("findingList");
  const filtered = findings.filter(item =>
    (severity === "All severities" || item.severity === severity) &&
    (!query || `${item.title} ${item.text} ${item.section}`.toLowerCase().includes(query))
  );
  list.innerHTML = filtered.length ? filtered.map(item => `
    <article class="finding severity-${item.severity}">
      <span class="finding-badge" style="color:${item.severity === "Critical" ? "#c65353" : item.severity === "High" ? "#df8a3a" : "#b2943f"}">${item.severity}</span>
      <div><strong>${item.title}</strong><small>${item.section} · ${item.text}</small></div>
      <span class="finding-status">${item.status}</span>
    </article>`).join("") : '<div class="empty-state">No findings match your filters.</div>';
}

function renderActions() {
  document.getElementById("actionTable").innerHTML = actions.map((item, index) => `
    <div class="action-table-row">
      <span>${String(index + 1).padStart(2,"0")}</span>
      <div><strong>${item.title}</strong><small>AI-Governance-Framework.pdf</small></div>
      <span class="owner">${item.owner}</span>
      <span>${item.due}</span>
      <span class="status-pill">${item.status}</span>
    </div>`).join("");
}

renderFindings();
renderActions();
document.getElementById("findingSearch").addEventListener("input", renderFindings);
document.getElementById("severityFilter").addEventListener("change", renderFindings);
document.getElementById("clearFilters").addEventListener("click", () => {
  document.getElementById("findingSearch").value = "";
  document.getElementById("severityFilter").value = "All severities";
  renderFindings();
});
document.querySelectorAll("[data-filter]").forEach(button => button.addEventListener("click", () => {
  switchTab("findings");
  document.getElementById("severityFilter").value = button.dataset.filter;
  renderFindings();
}));

document.querySelectorAll(".action-row input").forEach(input => input.addEventListener("change", () => {
  const row = input.closest(".action-row");
  row.style.opacity = input.checked ? ".55" : "1";
}));

const modal = document.getElementById("uploadModal");
const fileInput = document.getElementById("fileInput");
const startUpload = document.getElementById("startUpload");
const usecaseInput = document.getElementById("usecaseInput");
let selectedFile = null;
document.getElementById("openUpload").addEventListener("click", () => { modal.classList.add("open"); modal.setAttribute("aria-hidden","false"); });
document.getElementById("closeUpload").addEventListener("click", closeModal);
document.getElementById("cancelUpload").addEventListener("click", closeModal);
document.getElementById("openReview").addEventListener("click", () => switchTab("reading"));
function closeModal() { modal.classList.remove("open"); modal.setAttribute("aria-hidden","true"); }

const dropzone = document.getElementById("dropzone");
["dragenter","dragover"].forEach(name => dropzone.addEventListener(name, event => { event.preventDefault(); dropzone.classList.add("drag"); }));
["dragleave","drop"].forEach(name => dropzone.addEventListener(name, event => { event.preventDefault(); dropzone.classList.remove("drag"); }));
dropzone.addEventListener("drop", event => {
  if (event.dataTransfer.files.length) selectFile(event.dataTransfer.files[0]);
});
fileInput.addEventListener("change", () => { if (fileInput.files.length) selectFile(fileInput.files[0]); });
function selectFile(file) {
  selectedFile = file;
  document.getElementById("dropTitle").textContent = file.name;
  updateStartState();
}
usecaseInput.addEventListener("input", updateStartState);
function updateStartState() {
  startUpload.disabled = !selectedFile && usecaseInput.value.trim().length === 0;
}
startUpload.addEventListener("click", () => {
  closeModal();
  const toast = document.getElementById("toast");
  toast.classList.add("show");
  setTimeout(() => toast.classList.remove("show"), 3200);
  document.querySelector(".status-metric:nth-child(2) strong").textContent = selectedFile
    ? selectedFile.name
    : "Use-case text";
  document.getElementById("analyzedTime").textContent = "Just now";
});
