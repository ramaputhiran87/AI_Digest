import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import {
  LucideActivity,
  LucideArrowUpRight,
  LucideBell,
  LucideCalendarDays,
  LucideChartColumnIncreasing,
  LucideChartNoAxesCombined,
  LucideChartPie,
  LucideChevronDown,
  LucideChevronLeft,
  LucideChevronRight,
  LucideCircleAlert,
  LucideCircleCheck,
  LucideClock,
  LucideCloudUpload,
  LucideDownload,
  LucideEye,
  LucideFileCheck2,
  LucideFileSearch,
  LucideFileText,
  LucideFiles,
  LucideLayoutDashboard,
  LucideSearch,
  LucideSettings,
  LucideShieldCheck,
  LucideSparkles,
  LucideTrash2,
  LucideTrendingUp,
  LucideUpload,
  LucideX
} from '@lucide/angular';
import { EChartsOption } from 'echarts';
import { NgxEchartsDirective } from 'ngx-echarts';

interface Evidence {
  statementId: string;
  sourceText: string;
  sourceLineStart: number;
  sourceLineEnd: number;
  section: string;
}

interface Finding {
  id: string;
  findingType: string;
  agentName: string;
  title: string;
  description: string;
  priority: string;
  score: number;
  confidence: number;
  confidenceBand: string;
  evidence?: Evidence;
}

interface AiEvidence {
  quotation: string;
  startLine: number;
  endLine: number;
}

interface AiFinding {
  title: string;
  category: string;
  severity: string;
  status: string;
  observation: string;
  reasoning: string;
  likelihood: string;
  impact: string;
  exploitability: string;
  businessImpact: string;
  technicalImpact: string;
  recommendation: string;
  evidence: AiEvidence[];
  confidence: number;
  severityReason: string;
}

interface AiAnalysisReport {
  executiveSummary: string;
  documentUnderstanding: {
    purpose: string;
    domain: string;
    keyFacts: string[];
    components: string[];
    relationships: string[];
    summary: string;
  };
  securityAssessment: string;
  architectureAssessment: string;
  complianceAssessment: string;
  businessImpact: string;
  findings: AiFinding[];
  priorityActionPlan: string[];
  overallAssessment: string;
}

interface AiModelInfo {
  provider: string;
  modelName: string;
  modelVersion?: string;
  analysisStartedAt: string;
  analysisCompletedAt: string;
  inferenceCalls: number;
  inputTokens?: number;
  outputTokens?: number;
  status: string;
  findingOrigin: string;
}

interface DigestResult {
  documentTitle: string;
  documentType: string;
  analysisProvider?: string;
  modelInfo?: AiModelInfo;
  aiReport?: AiAnalysisReport;
  executiveSummary: string;
  highPriorityItems: Finding[];
  criticalRisks: Finding[];
  dependencies: Finding[];
  contradictions: { finding: Finding; evidenceA: Evidence; evidenceB: Evidence }[];
  humanReviewRequired: Finding[];
  recommendations: Finding[];
  keyFindings: Finding[];
  hiddenRisks: Finding[];
  futureRisks: Finding[];
  strategicOpportunities: Finding[];
  actionItems: Finding[];
  confidenceScore: number;
  confidenceBand: string;
  evidence: Evidence[];
  contradictionDetails: {
    subject: string;
    valueA: string;
    valueB: string;
    evidenceA: Evidence;
    evidenceB: Evidence;
    confidence: number;
  }[];
  compressedDocument: {
    fileName: string;
    markdown: string;
    statistics: {
      originalLines: number;
      compressedLines: number;
      removedLines: number;
      compressionPercent: number;
      noiseRemovedPercent: number;
    };
  };
}

type AnalysisStatus = 'Successful' | 'Failed';
type NavigationTab = 'Dashboard' | 'Document Analysis' | 'Analytics' | 'History';
type TrendPeriod = 'daily' | 'weekly' | 'monthly';
type HistorySort = 'date' | 'name' | 'risk';
type HistoryStatusFilter = 'All' | AnalysisStatus;
type AnalysisMode = 'Configured' | 'Deterministic';

interface AnalysisHistoryEntry {
  id: string;
  analysedAt: string;
  documentTitle: string;
  documentType: string;
  status: AnalysisStatus;
  result?: DigestResult;
  fileSize?: number;
  error?: string;
}

interface TrendBucket {
  key: string;
  label: string;
  successful: number;
  failed: number;
}

const HISTORY_STORAGE_KEY = 'ai-digest-analysis-history';
const MAX_HISTORY_ENTRIES = 20;
const SUPPORTED_EXTENSIONS = ['.md', '.txt', '.html', '.htm', '.json', '.xml'];
const NAV_ITEMS: NavigationTab[] = ['Dashboard', 'Document Analysis', 'Analytics', 'History'];

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    NgxEchartsDirective,
    LucideActivity,
    LucideArrowUpRight,
    LucideBell,
    LucideCalendarDays,
    LucideChartColumnIncreasing,
    LucideChartNoAxesCombined,
    LucideChartPie,
    LucideChevronDown,
    LucideChevronLeft,
    LucideChevronRight,
    LucideCircleAlert,
    LucideCircleCheck,
    LucideClock,
    LucideCloudUpload,
    LucideDownload,
    LucideEye,
    LucideFileCheck2,
    LucideFileSearch,
    LucideFileText,
    LucideFiles,
    LucideLayoutDashboard,
    LucideSearch,
    LucideSettings,
    LucideShieldCheck,
    LucideSparkles,
    LucideTrash2,
    LucideTrendingUp,
    LucideUpload,
    LucideX
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private readonly http = inject(HttpClient);
  readonly navItems = NAV_ITEMS;
  readonly Math = Math;
  readonly activeTab = signal<NavigationTab>('Dashboard');
  readonly useCase = signal('');
  readonly analysisMode = signal<AnalysisMode>('Configured');
  readonly fileName = signal('');
  readonly selectedFile = signal<File | null>(null);
  readonly loading = signal(false);
  readonly dragOver = signal(false);
  readonly apiError = signal('');
  readonly result = signal<DigestResult | null>(null);
  readonly history = signal<AnalysisHistoryEntry[]>([]);
  readonly notificationsOpen = signal(false);
  readonly settingsOpen = signal(false);
  readonly trendPeriod = signal<TrendPeriod>('daily');
  readonly historySearch = signal('');
  readonly historyStatus = signal<HistoryStatusFilter>('All');
  readonly historySort = signal<HistorySort>('date');
  readonly historyPage = signal(1);
  readonly historyPageSize = 8;
  readonly findingSeverity = signal('All');
  readonly findingCategory = signal('All');
  readonly minimumConfidence = signal(0);

  readonly findings = computed(() => {
    const data = this.result();
    if (!data) return [];
    const items = [
      ...data.criticalRisks,
      ...data.highPriorityItems,
      ...data.dependencies,
      ...data.humanReviewRequired,
      ...data.recommendations,
      ...data.hiddenRisks,
      ...data.futureRisks,
      ...data.strategicOpportunities,
      ...data.actionItems,
      ...data.keyFindings
    ];
    return items.filter((item, index, list) =>
      list.findIndex(candidate => candidate.id === item.id) === index);
  });

  readonly findingCategories = computed(() =>
    [...new Set((this.result()?.aiReport?.findings ?? []).map(finding => finding.category))]
      .sort((a, b) => a.localeCompare(b)));

  readonly visibleAiFindings = computed(() => {
    const findings = this.result()?.aiReport?.findings ?? [];
    const severity = this.findingSeverity();
    const category = this.findingCategory();
    const confidence = this.minimumConfidence() / 100;
    return findings.filter(finding =>
      (severity === 'All' || finding.severity.toUpperCase() === severity) &&
      (category === 'All' || finding.category === category) &&
      finding.confidence >= confidence);
  });

  readonly successfulHistory = computed(() =>
    this.history().filter(entry => entry.status === 'Successful' && entry.result));

  readonly failedCount = computed(() =>
    this.history().filter(entry => entry.status === 'Failed').length);

  readonly criticalRiskCount = computed(() =>
    this.successfulHistory().reduce((total, entry) =>
      total + this.findingsFor(entry.result!).filter(finding => finding.priority === 'Critical').length, 0));

  readonly averageScore = computed(() => {
    const entries = this.successfulHistory();
    if (!entries.length) return null;
    return Math.round(entries.reduce((total, entry) => total + entry.result!.confidenceScore, 0) / entries.length);
  });

  readonly filteredHistory = computed(() => {
    const query = this.historySearch().trim().toLowerCase();
    const status = this.historyStatus();
    const items = this.history().filter(entry => {
      const matchesQuery = !query ||
        entry.documentTitle.toLowerCase().includes(query) ||
        entry.documentType.toLowerCase().includes(query);
      return matchesQuery && (status === 'All' || entry.status === status);
    });

    return [...items].sort((a, b) => {
      if (this.historySort() === 'name') return a.documentTitle.localeCompare(b.documentTitle);
      if (this.historySort() === 'risk') return this.riskWeight(b) - this.riskWeight(a);
      return new Date(b.analysedAt).getTime() - new Date(a.analysedAt).getTime();
    });
  });

  readonly historyPageCount = computed(() =>
    Math.max(1, Math.ceil(this.filteredHistory().length / this.historyPageSize)));

  readonly pagedHistory = computed(() => {
    const start = (this.historyPage() - 1) * this.historyPageSize;
    return this.filteredHistory().slice(start, start + this.historyPageSize);
  });

  readonly trendBuckets = computed(() => this.buildTrendBuckets(this.trendPeriod()));

  readonly trendOption = computed<EChartsOption>(() => {
    const buckets = this.trendBuckets();
    return {
      animationDuration: 550,
      color: ['#2563eb', '#ffc928'],
      tooltip: { trigger: 'axis', axisPointer: { type: 'line' } },
      legend: { bottom: 0, icon: 'circle', textStyle: { color: '#64748b' } },
      grid: { left: 36, right: 18, top: 20, bottom: 50 },
      xAxis: {
        type: 'category',
        boundaryGap: false,
        data: buckets.map(bucket => bucket.label),
        axisLine: { lineStyle: { color: '#e5eaf2' } },
        axisLabel: { color: '#8290a5' }
      },
      yAxis: {
        type: 'value',
        minInterval: 1,
        splitLine: { lineStyle: { color: '#edf1f6', type: 'dashed' } },
        axisLabel: { color: '#8290a5' }
      },
      series: [
        {
          name: 'Successful',
          type: 'line',
          smooth: true,
          data: buckets.map(bucket => bucket.successful),
          symbol: 'circle',
          symbolSize: 7,
          lineStyle: { width: 3, color: '#2563eb' },
          itemStyle: { color: '#2563eb', borderColor: '#fff', borderWidth: 2 },
          areaStyle: { color: 'rgba(37, 99, 235, .09)' }
        },
        {
          name: 'Failed',
          type: 'line',
          smooth: true,
          data: buckets.map(bucket => bucket.failed),
          symbol: 'circle',
          symbolSize: 6,
          lineStyle: { width: 2, color: '#ffc928', type: 'dashed' },
          itemStyle: { color: '#ffc928' }
        }
      ]
    };
  });

  readonly riskDistribution = computed(() => {
    const counts = { Critical: 0, High: 0, Medium: 0, Low: 0 };
    for (const entry of this.successfulHistory()) {
      for (const finding of this.findingsFor(entry.result!)) {
        if (finding.priority in counts) {
          counts[finding.priority as keyof typeof counts]++;
        }
      }
    }
    return counts;
  });

  readonly riskOption = computed<EChartsOption>(() => {
    const counts = this.riskDistribution();
    const total = Object.values(counts).reduce((sum, value) => sum + value, 0);
    return {
      animationDuration: 550,
      color: ['#dc2626', '#f59e0b', '#ffc928', '#16a34a'],
      tooltip: {
        trigger: 'item',
        formatter: '{b}: {c} ({d}%)'
      },
      legend: {
        orient: 'vertical',
        right: 12,
        top: 'middle',
        itemWidth: 9,
        itemHeight: 9,
        textStyle: { color: '#64748b', fontSize: 12 }
      },
      graphic: total ? [] : [{
        type: 'text',
        left: 'center',
        top: 'center',
        style: { text: 'No findings yet', fill: '#94a3b8', fontSize: 13 }
      }],
      series: [{
        name: 'Risk distribution',
        type: 'pie',
        radius: ['57%', '78%'],
        center: ['34%', '50%'],
        avoidLabelOverlap: true,
        itemStyle: { borderRadius: 5, borderColor: '#fff', borderWidth: 3 },
        label: {
          show: true,
          position: 'center',
          formatter: total ? `{total|${total}}\\n{caption|findings}` : '',
          rich: {
            total: { fontSize: 23, fontWeight: 700, color: '#142b52', lineHeight: 30 },
            caption: { fontSize: 11, color: '#8290a5' }
          }
        },
        emphasis: { scale: true, scaleSize: 7 },
        data: Object.entries(counts).map(([name, value]) => ({ name, value }))
      }]
    };
  });

  readonly performanceBuckets = computed(() => this.buildTrendBuckets('weekly').slice(-6));

  readonly performanceOption = computed<EChartsOption>(() => {
    const buckets = this.performanceBuckets();
    return {
      animationDuration: 500,
      color: ['#2563eb', '#ffc928'],
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      legend: { bottom: 0, icon: 'circle', textStyle: { color: '#64748b' } },
      grid: { left: 36, right: 16, top: 18, bottom: 48 },
      xAxis: {
        type: 'category',
        data: buckets.map(bucket => bucket.label),
        axisLine: { lineStyle: { color: '#e5eaf2' } },
        axisLabel: { color: '#8290a5' }
      },
      yAxis: {
        type: 'value',
        minInterval: 1,
        splitLine: { lineStyle: { color: '#edf1f6', type: 'dashed' } },
        axisLabel: { color: '#8290a5' }
      },
      series: [
        { name: 'Successful', type: 'bar', barMaxWidth: 22, itemStyle: { borderRadius: [5, 5, 0, 0] }, data: buckets.map(bucket => bucket.successful) },
        { name: 'Failed', type: 'bar', barMaxWidth: 22, itemStyle: { borderRadius: [5, 5, 0, 0] }, data: buckets.map(bucket => bucket.failed) }
      ]
    };
  });

  readonly categoryCounts = computed(() => {
    const counts: Record<string, number> = {
      PDF: 0, TXT: 0, Markdown: 0, HTML: 0, JSON: 0, XML: 0
    };
    for (const entry of this.history()) {
      const category = this.documentCategory(entry);
      counts[category] = (counts[category] ?? 0) + 1;
    }
    return counts;
  });

  readonly categoryOption = computed<EChartsOption>(() => {
    const categories = Object.entries(this.categoryCounts());
    return {
      animationDuration: 500,
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      grid: { left: 76, right: 22, top: 16, bottom: 18 },
      xAxis: {
        type: 'value',
        minInterval: 1,
        splitLine: { lineStyle: { color: '#edf1f6', type: 'dashed' } },
        axisLabel: { color: '#8290a5' }
      },
      yAxis: {
        type: 'category',
        data: categories.map(([name]) => name),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: '#64748b' }
      },
      series: [{
        name: 'Documents',
        type: 'bar',
        barMaxWidth: 16,
        data: categories.map(([, value]) => value),
        itemStyle: {
          borderRadius: [0, 7, 7, 0],
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 1, y2: 0,
            colorStops: [
              { offset: 0, color: '#2563eb' },
              { offset: 1, color: '#78a5ff' }
            ]
          }
        }
      }]
    };
  });

  readonly totalChange = computed(() => this.comparePeriods(entries => entries.length));
  readonly successChange = computed(() =>
    this.comparePeriods(entries => entries.filter(entry => entry.status === 'Successful').length));
  readonly riskChange = computed(() => this.comparePeriods(
    items => items.reduce((total, entry) =>
      total + (entry.result ? this.findingsFor(entry.result).filter(finding => finding.priority === 'Critical').length : 0), 0)
  ));
  readonly scoreChange = computed(() => this.comparePeriods(entries => {
    const successful = entries.filter(entry => entry.result);
    return successful.length
      ? successful.reduce((total, entry) => total + entry.result!.confidenceScore, 0) / successful.length
      : null;
  }));
  readonly recentHistory = computed(() => this.history().slice(0, 5));

  readonly notificationCount = computed(() => this.failedCount());

  constructor() {
    this.loadHistory();
  }

  setTab(tab: NavigationTab) {
    this.activeTab.set(tab);
    this.notificationsOpen.set(false);
    this.settingsOpen.set(false);
  }

  selectTrendPeriod(period: TrendPeriod) {
    this.trendPeriod.set(period);
  }

  selectFile(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.acceptFile(file);
    input.value = '';
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.dragOver.set(true);
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.dragOver.set(false);
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.dragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.acceptFile(file);
  }

  removeFile() {
    this.selectedFile.set(null);
    this.fileName.set('');
  }

  analyse() {
    const file = this.selectedFile();
    const text = this.useCase().trim();
    if ((!file && !text) || this.loading()) return;

    this.loading.set(true);
    this.apiError.set('');
    if (file) {
      const form = new FormData();
      form.append('file', file);
      form.append('useCase', this.useCase());
      if (this.analysisMode() === 'Deterministic') {
        form.append('analysisMode', 'Deterministic');
      }
      this.sendAnalysis(
        this.http.post<DigestResult>('http://127.0.0.1:5050/api/documents/analyse', form),
        file.name,
        this.documentType(file.name),
        file.size
      );
      return;
    }

    this.sendAnalysis(
      this.http.post<DigestResult>('http://127.0.0.1:5050/api/documents/analyse-text', {
        text,
        title: 'Use-case.txt',
        useCase: this.useCase(),
        analysisMode: this.analysisMode() === 'Deterministic' ? 'Deterministic' : null
      }),
      'Use-case.txt',
      'Plain Text',
      new Blob([text]).size
    );
  }

  openHistory(entry: AnalysisHistoryEntry) {
    if (!entry.result || entry.status !== 'Successful') return;
    this.result.set(entry.result);
    this.activeTab.set('Document Analysis');
  }

  removeHistory(entry: AnalysisHistoryEntry) {
    this.persistHistory(this.history().filter(item => item.id !== entry.id));
  }

  viewHistoryEntry(entry: AnalysisHistoryEntry) {
    if (entry.result) {
      this.openHistory(entry);
      return;
    }
    this.apiError.set(entry.error || 'This analysis did not complete successfully.');
    this.activeTab.set('Document Analysis');
  }

  downloadHistoryReport(entry: AnalysisHistoryEntry) {
    if (!entry.result) return;
    const markdown = this.buildMarkdown(entry.result);
    this.download(markdown, 'text/markdown', this.reportName(entry.documentTitle));
  }

  jumpToSource(evidence: Evidence) {
    this.apiError.set(`Source reference: ${evidence.section}, lines ${evidence.sourceLineStart}–${evidence.sourceLineEnd}.`);
    this.activeTab.set('Document Analysis');
    document.getElementById('findings')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  exportJson() {
    const data = this.result();
    if (!data) return;
    this.download(JSON.stringify(data, null, 2), 'application/json', 'digest.json');
  }

  downloadCompressedMarkdown() {
    const data = this.result();
    if (!data) return;
    this.download(data.compressedDocument.markdown, 'text/markdown', data.compressedDocument.fileName);
  }

  exportMarkdown() {
    const data = this.result();
    if (!data) return;
    this.download(this.buildMarkdown(data), 'text/markdown', this.reportName(data.documentTitle));
  }

  setHistorySort(sort: HistorySort) {
    this.historySort.set(sort);
    this.historyPage.set(1);
  }

  setHistoryStatus(status: HistoryStatusFilter) {
    this.historyStatus.set(status);
    this.historyPage.set(1);
  }

  setHistorySearch(value: string) {
    this.historySearch.set(value);
    this.historyPage.set(1);
  }

  setFindingSeverity(value: string) {
    this.findingSeverity.set(value);
  }

  setFindingCategory(value: string) {
    this.findingCategory.set(value);
  }

  setMinimumConfidence(value: string) {
    const parsed = Number(value);
    this.minimumConfidence.set(Number.isFinite(parsed) ? Math.min(100, Math.max(0, parsed)) : 0);
  }

  previousHistoryPage() {
    this.historyPage.update(page => Math.max(1, page - 1));
  }

  nextHistoryPage() {
    this.historyPage.update(page => Math.min(this.historyPageCount(), page + 1));
  }

  formatBytes(bytes?: number) {
    if (bytes === undefined) return 'Text input';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  riskLevel(entry: AnalysisHistoryEntry) {
    if (!entry.result) return '—';
    const priorities = this.findingsFor(entry.result).map(finding => finding.priority);
    return priorities.includes('Critical') ? 'Critical' :
      priorities.includes('High') ? 'High' :
        priorities.includes('Medium') ? 'Medium' :
          priorities.includes('Low') ? 'Low' : 'None';
  }

  categoryPercent(category: string) {
    const counts = this.categoryCounts();
    const total = Object.values(counts).reduce((sum, value) => sum + value, 0);
    return total ? Math.round((counts[category] / total) * 100) : 0;
  }

  riskPercent(priority: string) {
    const counts = this.riskDistribution();
    const total = Object.values(counts).reduce((sum, value) => sum + value, 0);
    return total ? Math.round(((counts[priority as keyof typeof counts] ?? 0) / total) * 100) : 0;
  }

  toggleNotifications() {
    this.notificationsOpen.update(open => !open);
    this.settingsOpen.set(false);
  }

  toggleSettings() {
    this.settingsOpen.update(open => !open);
    this.notificationsOpen.set(false);
  }

  private acceptFile(file: File) {
    const extension = this.extension(file.name);
    if (!SUPPORTED_EXTENSIONS.includes(extension)) {
      this.apiError.set('Unsupported file type. Choose MD, TXT, HTML, JSON, or XML.');
      return;
    }
    if (file.size > 20 * 1024 * 1024) {
      this.apiError.set('This file exceeds the 20 MB upload limit.');
      return;
    }
    this.apiError.set('');
    this.selectedFile.set(file);
    this.fileName.set(file.name);
  }

  private sendAnalysis(
    request: Observable<DigestResult>,
    title: string,
    type: string,
    size: number
  ) {
    request.subscribe({
      next: value => this.setResult(value, title, type, size),
      error: error => {
        const payload = error?.error as { error?: unknown; detail?: unknown } | undefined;
        const providerMessage = typeof payload?.error === 'string' ? payload.error : '';
        const providerDetail = typeof payload?.detail === 'string' ? payload.detail : '';
        const message = providerMessage
          ? `${providerMessage}${providerDetail ? ` ${providerDetail}` : ''}`
          : error.status
            ? `Analysis failed (${error.status}). Check that the API is running on port 5050.`
            : 'Analysis API is unavailable.';
        this.apiError.set(message);
        this.recordFailure(title, type, size, message);
        this.loading.set(false);
      },
      complete: () => this.loading.set(false)
    });
  }

  private setResult(value: DigestResult, title: string, type: string, size: number) {
    this.result.set(value);
    this.activeTab.set('Document Analysis');
    this.persistHistory([{
      id: crypto.randomUUID(),
      analysedAt: new Date().toISOString(),
      documentTitle: value.documentTitle || title,
      documentType: value.documentType || type,
      status: 'Successful' as const,
      result: value,
      fileSize: size
    }, ...this.history()].slice(0, MAX_HISTORY_ENTRIES));
  }

  private recordFailure(title: string, type: string, size: number, error: string) {
    this.persistHistory([{
      id: crypto.randomUUID(),
      analysedAt: new Date().toISOString(),
      documentTitle: title,
      documentType: type,
      status: 'Failed' as const,
      fileSize: size,
      error
    }, ...this.history()].slice(0, MAX_HISTORY_ENTRIES));
  }

  private loadHistory() {
    try {
      const saved = localStorage.getItem(HISTORY_STORAGE_KEY);
      if (!saved) return;
      const parsed: unknown = JSON.parse(saved);
      if (!Array.isArray(parsed)) throw new Error('Saved history is not a list.');

      this.history.set(parsed
        .filter((entry): entry is AnalysisHistoryEntry => this.isHistoryEntry(entry))
        .map(entry => ({
          ...entry,
          status: entry.status ?? 'Successful',
          documentType: entry.documentType || entry.result?.documentType || this.documentType(entry.documentTitle)
        }))
        .slice(0, MAX_HISTORY_ENTRIES));
    } catch {
      this.apiError.set('Saved analysis history could not be loaded from this browser.');
    }
  }

  private persistHistory(entries: AnalysisHistoryEntry[]) {
    try {
      localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify(entries));
      this.history.set(entries);
    } catch {
      this.apiError.set('The analysis is complete, but browser history could not be saved.');
    }
  }

  private isHistoryEntry(value: unknown): value is AnalysisHistoryEntry {
    if (typeof value !== 'object' || value === null) return false;
    const entry = value as Partial<AnalysisHistoryEntry>;
    if (typeof entry.id !== 'string' ||
      typeof entry.analysedAt !== 'string' ||
      typeof entry.documentTitle !== 'string') return false;

    if (entry.status === 'Failed') return true;
    const result = entry.result;
    return typeof result?.documentTitle === 'string' &&
      Array.isArray(result.keyFindings) &&
      typeof result.confidenceScore === 'number' &&
      typeof result.compressedDocument?.markdown === 'string';
  }

  private findingsFor(result: DigestResult) {
    const groups = [
      result.criticalRisks ?? [],
      result.highPriorityItems ?? [],
      result.dependencies ?? [],
      result.humanReviewRequired ?? [],
      result.recommendations ?? [],
      result.hiddenRisks ?? [],
      result.futureRisks ?? [],
      result.strategicOpportunities ?? [],
      result.actionItems ?? [],
      result.keyFindings ?? []
    ];
    const findings = groups.flat();
    return findings.filter((item, index, list) =>
      list.findIndex(candidate => candidate.id === item.id) === index);
  }

  private buildTrendBuckets(period: TrendPeriod): TrendBucket[] {
    const now = new Date();
    const buckets: TrendBucket[] = [];

    if (period === 'daily') {
      for (let offset = 6; offset >= 0; offset--) {
        const date = new Date(now.getFullYear(), now.getMonth(), now.getDate() - offset);
        buckets.push({
          key: this.dateKey(date),
          label: new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(date),
          successful: 0,
          failed: 0
        });
      }
    } else if (period === 'weekly') {
      const currentMonday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
      currentMonday.setDate(currentMonday.getDate() - ((currentMonday.getDay() + 6) % 7));
      for (let offset = 7; offset >= 0; offset--) {
        const start = new Date(currentMonday);
        start.setDate(start.getDate() - offset * 7);
        buckets.push({
          key: this.dateKey(start),
          label: new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(start),
          successful: 0,
          failed: 0
        });
      }
    } else {
      for (let offset = 5; offset >= 0; offset--) {
        const date = new Date(now.getFullYear(), now.getMonth() - offset, 1);
        buckets.push({
          key: `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`,
          label: new Intl.DateTimeFormat(undefined, { month: 'short' }).format(date),
          successful: 0,
          failed: 0
        });
      }
    }

    const bucketByKey = new Map(buckets.map(bucket => [bucket.key, bucket]));
    for (const entry of this.history()) {
      const date = new Date(entry.analysedAt);
      if (Number.isNaN(date.getTime())) continue;
      let key = this.dateKey(date);
      if (period === 'weekly') {
        const monday = new Date(date.getFullYear(), date.getMonth(), date.getDate());
        monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
        key = this.dateKey(monday);
      } else if (period === 'monthly') {
        key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      }

      const bucket = bucketByKey.get(key);
      if (!bucket) continue;
      if (entry.status === 'Successful') bucket.successful++;
      else bucket.failed++;
    }
    return buckets;
  }

  private comparePeriods<T>(selector: (entries: AnalysisHistoryEntry[]) => T): {
    label: string;
    positive: boolean;
  } | null {
    const now = new Date();
    const currentStart = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6);
    const previousStart = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 13);
    const previousEnd = new Date(currentStart.getTime() - 1);
    const currentEntries = this.history().filter(entry => new Date(entry.analysedAt) >= currentStart);
    const previousEntries = this.history().filter(entry => {
      const date = new Date(entry.analysedAt);
      return date >= previousStart && date <= previousEnd;
    });
    if (!previousEntries.length) return currentEntries.length ? { label: 'New', positive: true } : null;

    const currentValue = selector(currentEntries);
    const previousValue = selector(previousEntries);
    if (typeof currentValue !== 'number' || typeof previousValue !== 'number') return null;
    if (previousValue === 0) {
      return currentValue === 0 ? { label: '0%', positive: true } : { label: 'New', positive: true };
    }
    const change = Math.round(((currentValue - previousValue) / previousValue) * 100);
    return {
      label: `${change > 0 ? '+' : ''}${change}%`,
      positive: change >= 0
    };
  }

  private riskWeight(entry: AnalysisHistoryEntry) {
    if (!entry.result) return -1;
    return this.findingsFor(entry.result).reduce((weight, finding) =>
      weight + (finding.priority === 'Critical' ? 4 :
        finding.priority === 'High' ? 3 :
          finding.priority === 'Medium' ? 2 :
            finding.priority === 'Low' ? 1 : 0), 0);
  }

  private documentCategory(entry: AnalysisHistoryEntry) {
    const extension = this.extension(entry.documentTitle);
    if (extension === '.md') return 'Markdown';
    if (extension === '.htm' || extension === '.html') return 'HTML';
    if (extension === '.json') return 'JSON';
    if (extension === '.xml') return 'XML';
    if (extension === '.pdf') return 'PDF';
    return 'TXT';
  }

  documentType(title: string) {
    const extension = this.extension(title);
    if (extension === '.md') return 'Markdown';
    if (extension === '.htm' || extension === '.html') return 'HTML';
    if (extension === '.json') return 'JSON';
    if (extension === '.xml') return 'XML';
    if (extension === '.pdf') return 'PDF';
    return 'Plain Text';
  }

  private extension(name: string) {
    const dot = name.lastIndexOf('.');
    return dot < 0 ? '' : name.slice(dot).toLowerCase();
  }

  private dateKey(date: Date) {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  private buildMarkdown(data: DigestResult) {
    const report = data.aiReport;
    if (report) {
      const model = data.modelInfo;
      const severityCounts = report.findings.reduce<Record<string, number>>((counts, finding) => {
        counts[finding.severity] = (counts[finding.severity] ?? 0) + 1;
        return counts;
      }, {});
      return [
        `# AI Executive Review: ${data.documentTitle}`,
        '',
        `**Document type:** ${data.documentType}`,
        `**Provider / model:** ${model?.provider ?? data.analysisProvider ?? 'Unknown'} / ${model?.modelName ?? 'Unknown'}`,
        `**Model version:** ${model?.modelVersion ?? 'Not reported'}`,
        `**Analysis status:** ${model?.status ?? 'Completed'}`,
        `**Inference calls:** ${model?.inferenceCalls ?? 'Not available'}`,
        '',
        '## Executive Summary',
        report.executiveSummary,
        '',
        '## Document Understanding',
        `**Purpose:** ${report.documentUnderstanding.purpose}`,
        `**Domain:** ${report.documentUnderstanding.domain}`,
        report.documentUnderstanding.summary,
        '',
        '### Key facts',
        ...report.documentUnderstanding.keyFacts.map(item => `- ${item}`),
        '',
        '### Components and relationships',
        ...report.documentUnderstanding.components.map(item => `- Component: ${item}`),
        ...report.documentUnderstanding.relationships.map(item => `- Relationship: ${item}`),
        '',
        '## Key Findings',
        ...(report.findings.length
          ? report.findings.flatMap(finding => [
              `### ${finding.severity}: ${finding.title}`,
              `**Category/status:** ${finding.category} / ${finding.status}`,
              `**Observation:** ${finding.observation}`,
              `**Reasoning:** ${finding.reasoning}`,
              `**Likelihood:** ${finding.likelihood}`,
              `**Potential impact:** ${finding.impact}`,
              `**Exploitability:** ${finding.exploitability}`,
              `**Severity rationale:** ${finding.severityReason}`,
              `**Confidence:** ${(finding.confidence * 100).toFixed(0)}%`,
              `**Business impact:** ${finding.businessImpact}`,
              `**Technical impact:** ${finding.technicalImpact}`,
              `**Recommendation:** ${finding.recommendation}`,
              ...finding.evidence.map(evidence =>
                `> ${evidence.quotation}\n>\n> Source lines ${evidence.startLine}-${evidence.endLine}`),
              ''
            ])
          : ['No evidence-backed findings were identified.', '']),
        '## Security Assessment',
        report.securityAssessment,
        '',
        '## Architecture Assessment',
        report.architectureAssessment,
        '',
        '## Compliance Assessment',
        report.complianceAssessment,
        '',
        '## Business Impact',
        report.businessImpact,
        '',
        '## Risk Distribution',
        ...['CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'INFORMATIONAL']
          .map(severity => `- ${severity}: ${severityCounts[severity] ?? 0}`),
        '',
        '## Priority Action Plan',
        ...(report.priorityActionPlan.length
          ? report.priorityActionPlan.map((action, index) => `${index + 1}. ${action}`)
          : ['No priority actions were generated.']),
        '',
        '## Overall AI Assessment',
        report.overallAssessment,
        '',
        '## Evidence and Review Status',
        model?.findingOrigin ?? 'AI-generated assessment; source quotations are checked programmatically.',
        'Source verification checks quotation traceability only. Findings have not been human-verified and do not establish that a real-world system is exploitable.'
      ].join('\n');
    }

    const findings = this.findingsFor(data);
    return [
      `# Executive Review: ${data.documentTitle}`,
      '',
      data.executiveSummary,
      '',
      ...findings.map(finding =>
        `## ${finding.priority}: ${finding.title}\n\n${finding.description}\n\nAgent: ${finding.agentName} · Confidence: ${finding.confidence}% · Source: ${finding.evidence?.section}, line ${finding.evidence?.sourceLineStart}\n\n> ${finding.evidence?.sourceText ?? ''}`)
    ].join('\n');
  }

  private reportName(title: string) {
    return `${title.replace(/\.[^.]+$/, '')}_Review.md`;
  }

  private download(content: string, type: string, name: string) {
    const url = URL.createObjectURL(new Blob([content], { type }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
