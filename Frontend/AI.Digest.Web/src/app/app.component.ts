import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, Subscription } from 'rxjs';
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
  LucideMonitor,
  LucidePresentation,
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
import { PresentationChartComponent } from './presentation-chart.component';
import { PresentationDiagramComponent } from './presentation-diagram.component';
import { PresentationTableComponent } from './presentation-table.component';

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

interface AnalysisProgressEvent {
  stage: string;
  completedChunks: number;
  totalChunks: number;
  completedInferenceCalls: number;
  plannedInferenceCalls: number | null;
  provider: string | null;
  model: string | null;
  message: string | null;
  completed: boolean;
  cancelled: boolean;
  failed: boolean;
}

interface AiAnalysisReport {
  sourceType?: string;
  executiveSummary: string;
  documentUnderstanding: {
    purpose: string;
    domain: string;
    keyFacts: string[];
    components: string[];
    relationships: string[];
    summary: string;
  };
  sections?: AnalysisContentSection[];
  securityAssessment: string;
  architectureAssessment: string;
  complianceAssessment: string;
  businessImpact: string;
  findings: AiFinding[];
  priorityActionPlan: string[];
  overallAssessment: string;
}

interface AnalysisContentSection {
  id: string;
  title: string;
  type: string;
  content: string;
  origin: string;
  evidence: AiEvidence[];
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
  warnings?: string[];
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
type NavigationTab = 'Dashboard' | 'Document Analysis' | 'Presentation Studio' | 'Analytics' | 'History';
type TrendPeriod = 'daily' | 'weekly' | 'monthly';
type HistorySort = 'date' | 'name' | 'risk';
type HistoryStatusFilter = 'All' | AnalysisStatus;
type AnalysisMode = 'Configured' | 'Gemini' | 'Groq' | 'Deterministic';

interface PresentationSlide {
  sectionId?: string;
  slideNumber: number;
  title: string;
  subtitle: string;
  layoutType: string;
  content: string[];
  visualElements: { type: string; label: string; value: string }[];
  diagram?: { type: string; mermaid: string };
  chart?: { type: string; labels: string[]; values: number[] };
  table?: { headers: string[]; rows: string[][] };
  screenshotReferences: string[];
  speakerNotes: string;
  sourceReferences: string[];
  fontFamily: string;
  backgroundColor: string;
  textColor: string;
  accentColor: string;
  contentScale: number;
  contentAlignment: string;
  contentSpacing: number;
  imageDataUrl?: string;
}

interface PresentationDocument {
  id: string;
  title: string;
  description: string;
  sourceType: string;
  sourceReference: string;
  targetAudience: string;
  theme: string;
  language?: string;
  themeRecommendation?: string;
  aspectRatio: string;
  totalSlides: number;
  createdAt: string;
  aiProvider: string;
  aiModel: string;
  status: string;
  slides: PresentationSlide[];
}

interface PresentationEditProposal {
  summary: string;
  provider: string;
  model: string;
  presentation: PresentationDocument;
}

interface PresentationVersionSummary {
  id: string;
  createdAt: string;
  label: string;
}

interface PresentationVersion extends PresentationVersionSummary {
  presentation: PresentationDocument;
}
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
const NAV_ITEMS: NavigationTab[] = ['Dashboard', 'Document Analysis', 'Presentation Studio', 'Analytics', 'History'];

interface AiServiceStatus {
  serviceStatus: 'Online' | 'Offline';
  activeProvider: string | null;
  activeProviderName: string | null;
  activeModel: string | null;
  connectionStatus: string;
  fallbackProviderStatus: string;
  lastSuccessfulRequest: { provider: string; occurredAt: string } | null;
  canTestConnection: boolean;
  requiresExplicitCloudConsent?: boolean;
  providers?: { inferenceAllowed: boolean }[];
}

interface CloudAiConsentStatus {
  isConsented: boolean;
  grantedAt: string | null;
  scope: string;
  policyVersion: string;
  withdrawnAt: string | null;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    NgxEchartsDirective,
    PresentationChartComponent,
    PresentationDiagramComponent,
    PresentationTableComponent,
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
    LucideMonitor,
      LucidePresentation,
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
  private readonly apiBaseUrl = `http://${globalThis.location.hostname}:5050`;
  readonly navItems = NAV_ITEMS;
  readonly Math = Math;
  readonly activeTab = signal<NavigationTab>('Dashboard');
  readonly sidebarCollapsed = signal(globalThis.innerWidth < 820);
  readonly useCase = signal('');
  readonly analysisMode = signal<AnalysisMode>('Configured');
  readonly analysisDepth = signal<'Quick' | 'Smart' | 'Deep'>('Smart');
  readonly fileName = signal('');
  readonly selectedFile = signal<File | null>(null);
  readonly loading = signal(false);
  readonly dragOver = signal(false);
  readonly apiError = signal('');
  readonly result = signal<DigestResult | null>(null);
  readonly originalResult = signal<DigestResult | null>(null);
  readonly analysisEditMode = signal(false);
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
  readonly presentationSource = signal<'Markdown' | 'GitHub' | 'ApplicationUrl' | 'UseCase' | 'AnalysisResult'>('Markdown');
  readonly presentationFile = signal<File | null>(null);
  readonly presentationSourceReference = signal('');
  readonly presentationUseCase = signal('');
  readonly presentationTitle = signal('AI Document Intelligence Overview');
  readonly presentationAudience = signal('Enterprise stakeholders');
  readonly presentationPurpose = signal('Explain the solution, architecture, benefits and roadmap');
  readonly presentationSlideCount = signal(10);
  readonly presentationLanguage = signal('English');
  readonly presentationAspectRatio = signal('16:9');
  readonly presentationMode = signal('InteractiveStorytelling');
  readonly presentationDuration = signal('15 minutes');
  readonly presentationStyle = signal('Visual storytelling');
  readonly presentationContentDepth = signal('Standard');
  readonly presentationTheme = signal('Prism Blue');
  readonly presentationInstructions = signal('');
  readonly presentationVisualIntensity = signal('Balanced');
  readonly includeSpeakerNotes = signal(true);
  readonly includeArchitectureDiagrams = signal(true);
  readonly includeScreenshots = signal(true);
  readonly includeFlowcharts = signal(true);
  readonly includeDataFlowDiagrams = signal(true);
  readonly includeCharts = signal(true);
  readonly includeProblemSolution = signal(true);
  readonly includeTechnicalDetails = signal(true);
  readonly includeBusinessRecommendations = signal(true);
  readonly includeRoadmap = signal(true);
  readonly presentationLoading = signal(false);
  readonly presentationError = signal('');
  readonly presentation = signal<PresentationDocument | null>(null);
  readonly presentationUndoStack = signal<PresentationDocument[]>([]);
  readonly presentationRedoStack = signal<PresentationDocument[]>([]);
  readonly presentationDirty = signal(false);
  readonly presentationSaveError = signal('');
  readonly presentationSaving = signal(false);
  readonly presentationPreviewIndex = signal(0);
  readonly presentationZoom = signal(1);
  readonly presentationAlignmentGuides = signal(false);
  readonly draggedPresentationSlide = signal<number | null>(null);
  readonly presentationHistory = signal<PresentationDocument[]>([]);
  readonly presentationVersions = signal<PresentationVersionSummary[]>([]);
  readonly presentationOriginal = signal<PresentationDocument | null>(null);
  readonly presentationCompareVersion = signal<PresentationVersion | null>(null);
  readonly aiEditScope = signal<'SelectedElement' | 'SelectedSlide' | 'SelectedSlides' | 'EntirePresentation'>('SelectedSlide');
  readonly aiEditElement = signal('Content');
  readonly selectedPresentationSlides = signal<string[]>([]);
  readonly aiEditInstruction = signal('');
  readonly aiEditLoading = signal(false);
  readonly aiEditError = signal('');
  readonly aiEditProposal = signal<{
    summary: string;
    provider: string;
    model: string;
    before: PresentationDocument;
    after: PresentationDocument;
    scope: string;
    element: string;
    sectionIds: string[];
  } | null>(null);
  private presentationAutosaveTimer: ReturnType<typeof setTimeout> | null = null;
  private presentationSaveInProgress = false;
  readonly analysisElapsedSeconds = signal(0);
  readonly analysisProgress = signal<AnalysisProgressEvent | null>(null);
  readonly analysisStages = signal<string[]>([]);
  readonly analysisProgressUnavailable = signal(false);
  private analysisSubscription: Subscription | null = null;
  private analysisProgressStream: EventSource | null = null;
  private analysisTimer: ReturnType<typeof setInterval> | null = null;
  readonly presentationViewerOpen = signal(false);
  readonly presentationViewerMode = signal<'Linear' | 'Scroll'>('Linear');
  readonly presentationOverviewOpen = signal(false);
  readonly presentationNotesOpen = signal(false);
  readonly presentationTimerRunning = signal(false);
  readonly presentationElapsedSeconds = signal(0);
  readonly presentationRemainingSeconds = signal(15 * 60);
  private presentationTimer: ReturnType<typeof setInterval> | null = null;
  readonly aiServiceStatus = signal<AiServiceStatus | null>(null);
  readonly aiServiceStatusLoading = signal(true);
  readonly aiServiceStatusError = signal('');
  readonly aiConnectionTestStatus = signal('');
  readonly cloudConsentStatus = signal<CloudAiConsentStatus | null>(null);
  readonly cloudConsentLoading = signal(true);
  readonly cloudConsentSaving = signal(false);
  readonly cloudConsentDialogOpen = signal(false);
  readonly cloudConsentChecked = signal(false);
  readonly cloudConsentError = signal('');
  private pendingCloudConsentAction: (() => void) | null = null;

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

  readonly inferenceCallCount = computed(() =>
    this.successfulHistory().reduce(
      (total, entry) => total + (entry.result?.modelInfo?.inferenceCalls ?? 0),
      0));

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
  readonly recentHistory = computed(() => this.history().slice(0, 5));

  readonly notificationCount = computed(() => this.failedCount());
  readonly presentationPreviewSlide = computed(() => {
    const result = this.presentation();
    if (!result?.slides.length) return null;
    const index = Math.min(Math.max(this.presentationPreviewIndex(), 0), result.slides.length - 1);
    return result.slides[index];
  });

  constructor() {
    this.loadHistory();
    this.refreshAiStatus();
    this.refreshCloudConsent();
  }

  setTab(tab: NavigationTab) {
    this.activeTab.set(tab);
    if (tab === 'Presentation Studio') this.loadPresentationHistory();
    if (tab === 'Dashboard') this.refreshAiStatus();
    this.notificationsOpen.set(false);
    this.settingsOpen.set(false);
  }

  toggleSidebar() {
    this.sidebarCollapsed.update(collapsed => !collapsed);
  }

  refreshAiStatus() {
    this.aiServiceStatusLoading.set(true);
    this.aiServiceStatusError.set('');
    this.http.get<AiServiceStatus>(`${this.apiBaseUrl}/api/ai/status`).subscribe({
      next: status => {
        this.aiServiceStatus.set(status);
        this.aiServiceStatusLoading.set(false);
      },
      error: () => {
        this.aiServiceStatus.set(null);
        this.aiServiceStatusError.set('AI service status is currently unavailable.');
        this.aiServiceStatusLoading.set(false);
      }
    });
  }

  refreshCloudConsent() {
    this.cloudConsentLoading.set(true);
    this.http.get<CloudAiConsentStatus>(
      `${this.apiBaseUrl}/api/consent/cloud-ai/`,
      { withCredentials: true }
    ).subscribe({
      next: status => {
        this.cloudConsentStatus.set(status);
        this.cloudConsentLoading.set(false);
        this.resumePendingCloudAction();
      },
      error: () => {
        this.cloudConsentStatus.set(null);
        this.cloudConsentLoading.set(false);
        this.resumePendingCloudAction();
      }
    });
  }

  reviewCloudConsent() {
    this.pendingCloudConsentAction = null;
    this.cloudConsentChecked.set(false);
    this.cloudConsentError.set('');
    this.cloudConsentDialogOpen.set(true);
  }

  cancelCloudConsent() {
    this.pendingCloudConsentAction = null;
    this.cloudConsentDialogOpen.set(false);
    this.cloudConsentChecked.set(false);
    this.cloudConsentError.set('');
  }

  grantCloudConsent() {
    if (!this.cloudConsentChecked() || this.cloudConsentSaving()) return;

    const current = this.cloudConsentStatus();
    const expectedScope = current?.scope ?? 'gemini-and-groq-cloud-document-analysis-and-presentation';
    const expectedPolicyVersion = current?.policyVersion ?? '1';
    this.cloudConsentSaving.set(true);
    this.cloudConsentError.set('');
    this.http.post<CloudAiConsentStatus>(
      `${this.apiBaseUrl}/api/consent/cloud-ai/`,
      {
        consented: true,
        scope: expectedScope,
        policyVersion: expectedPolicyVersion
      },
      { withCredentials: true }
    ).subscribe({
      next: status => {
        if (!status?.isConsented ||
            status.scope !== expectedScope ||
            status.policyVersion !== expectedPolicyVersion ||
            status.withdrawnAt) {
          this.cloudConsentSaving.set(false);
          this.cloudConsentError.set('The server did not confirm this consent. Please try again.');
          return;
        }
        this.cloudConsentStatus.set(status);
        this.cloudConsentSaving.set(false);
        this.cloudConsentDialogOpen.set(false);
        this.cloudConsentChecked.set(false);
        this.cloudConsentError.set('');
        this.resumePendingCloudAction();
      },
      error: error => {
        this.cloudConsentSaving.set(false);
        this.cloudConsentError.set(error.status === 0
          ? 'Could not reach the consent service. Check the backend connection and try again.'
          : error.status === 401
            ? 'Your session is not authorized. Sign in again and retry.'
            : error.status === 403
              ? 'This session is not authorized to save consent.'
              : error.status === 404
                ? 'The consent service endpoint is unavailable. Restart the backend and retry.'
                : error.status === 429
                  ? 'Too many consent requests. Please wait and try again.'
                  : error.status === 503
                    ? 'Consent could not be stored on the server. Please try again later.'
                    : error.status === 400
                      ? 'The consent policy changed. Refresh the application and review it again.'
                      : 'Consent could not be saved. Please try again.');
      }
    });
  }

  withdrawCloudConsent() {
    this.http.delete<CloudAiConsentStatus>(
      `${this.apiBaseUrl}/api/consent/cloud-ai/`,
      { withCredentials: true }
    ).subscribe({
      next: status => this.cloudConsentStatus.set(status),
      error: () => this.cloudConsentError.set('Consent could not be withdrawn. Please try again.')
    });
  }

  private requireCloudConsent(action: () => void): boolean {
    if (this.analysisMode() === 'Deterministic') return true;

    const serviceStatus = this.aiServiceStatus();
    const hasConfiguredProvider = serviceStatus?.providers
      ? serviceStatus.providers.some(provider => provider.inferenceAllowed)
      : true;
    if (serviceStatus?.requiresExplicitCloudConsent === false || !hasConfiguredProvider)
      return true;
    if (this.cloudConsentLoading()) {
      this.pendingCloudConsentAction = action;
      return false;
    }
    if (this.cloudConsentStatus()?.isConsented) return true;

    this.pendingCloudConsentAction = action;
    this.cloudConsentChecked.set(false);
    this.cloudConsentError.set('');
    this.cloudConsentDialogOpen.set(true);
    return false;
  }

  private resumePendingCloudAction() {
    const action = this.pendingCloudConsentAction;
    if (!action || this.cloudConsentLoading()) return;
    this.pendingCloudConsentAction = null;
    if (this.cloudConsentStatus()?.isConsented) {
      action();
      return;
    }
    this.cloudConsentChecked.set(false);
    this.cloudConsentDialogOpen.set(true);
  }

  testAiConnection() {
    const status = this.aiServiceStatus();
    const providerId = status?.activeProvider?.toLowerCase();
    if (!status?.canTestConnection || (providerId !== 'gemini' && providerId !== 'groq'))
      return;

    this.aiConnectionTestStatus.set('');
    this.http.post<{ status: string }>(
      `${this.apiBaseUrl}/api/admin/ai/providers/${providerId}/test`,
      {}
    ).subscribe({
      next: result => {
        this.aiConnectionTestStatus.set(result.status);
        this.refreshAiStatus();
      },
      error: () => {
        this.aiConnectionTestStatus.set(
          'Connection test failed. Check administrator authorization and backend status.'
        );
        this.refreshAiStatus();
      }
    });
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
    if (!this.requireCloudConsent(() => this.analyse())) return;

    this.loading.set(true);
    this.analysisElapsedSeconds.set(0);
    this.analysisProgress.set(null);
    this.analysisStages.set([]);
    this.analysisProgressUnavailable.set(false);
    this.startAnalysisTimer();
    this.apiError.set('');
    const progressId = globalThis.crypto.randomUUID();
    this.openAnalysisProgress(progressId);
    if (file) {
      const form = new FormData();
      form.append('file', file);
      form.append('progressId', progressId);
      form.append('useCase', this.useCase());
      form.append('analysisDepth', this.analysisDepth());
      form.append('cloudConsentScope', this.cloudConsentStatus()?.scope ?? '');
      form.append('cloudConsentPolicyVersion', this.cloudConsentStatus()?.policyVersion ?? '');
      if (this.analysisMode() !== 'Configured') {
        form.append('analysisMode', this.analysisMode());
      }
      this.sendAnalysis(
        this.http.post<DigestResult>(
          `${this.apiBaseUrl}/api/documents/analyse`,
          form,
          { withCredentials: true }
        ),
        file.name,
        this.documentType(file.name),
        file.size
      );
      return;
    }

    this.sendAnalysis(
      this.http.post<DigestResult>(
        `${this.apiBaseUrl}/api/documents/analyse-text`,
        {
          text,
          title: 'Use-case.txt',
          useCase: this.useCase(),
          progressId,
          analysisDepth: this.analysisDepth(),
          cloudConsentScope: this.cloudConsentStatus()?.scope ?? '',
          cloudConsentPolicyVersion: this.cloudConsentStatus()?.policyVersion ?? '',
          analysisMode: this.analysisMode() === 'Configured' ? null : this.analysisMode()
        },
        { withCredentials: true }
      ),
      'Use-case.txt',
      'Plain Text',
      new Blob([text]).size
    );
  }

  openHistory(entry: AnalysisHistoryEntry) {
    if (!entry.result || entry.status !== 'Successful') return;
    this.result.set(entry.result);
    this.originalResult.set(entry.result);
    this.analysisEditMode.set(false);
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
    this.apiError.set(`Source reference: ${evidence.section}, lines ${evidence.sourceLineStart}ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“${evidence.sourceLineEnd}.`);
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

  selectPresentationSource(source: 'Markdown' | 'GitHub' | 'ApplicationUrl' | 'UseCase' | 'AnalysisResult') {
    this.presentationSource.set(source);
  }

  updateAnalysisExecutiveSummary(value: string) {
    const current = this.result();
    if (!current?.aiReport) return;
    this.updateAnalysisResult({
      ...current,
      executiveSummary: value,
      aiReport: { ...current.aiReport, executiveSummary: value }
    });
  }

  updateAnalysisSection(sectionId: string, value: string) {
    const current = this.result();
    if (!current?.aiReport?.sections) return;
    this.updateAnalysisResult({
      ...current,
      aiReport: {
        ...current.aiReport,
        sections: current.aiReport.sections.map(section =>
          section.id === sectionId
            ? { ...section, content: value, origin: 'UserEdited' }
            : section)
      }
    });
  }

  confirmAnalysisSection(sectionId: string) {
    const current = this.result();
    if (!current?.aiReport?.sections) return;
    this.updateAnalysisResult({
      ...current,
      aiReport: {
        ...current.aiReport,
        sections: current.aiReport.sections.map(section =>
          section.id === sectionId
            ? { ...section, origin: 'UserConfirmed' }
            : section)
      }
    });
  }

  removeAnalysisSection(sectionId: string) {
    const current = this.result();
    if (!current?.aiReport?.sections) return;
    this.updateAnalysisResult({
      ...current,
      aiReport: {
        ...current.aiReport,
        sections: current.aiReport.sections.filter(section => section.id !== sectionId)
      }
    });
  }

  analysisOriginLabel(origin: string) {
    return ({
      SourceConfirmed: 'Source-confirmed',
      UserConfirmed: 'User-confirmed',
      AIElaboration: 'AI elaboration',
      Suggested: 'Suggested',
      Assumption: 'Assumption',
      RequiresClarification: 'Requires clarification',
      NotSpecified: 'Not specified',
      UserEdited: 'User-edited'
    } as Record<string, string>)[origin] ?? origin;
  }

  isRiskCategory(category: string) {
    return ['Security', 'Compliance', 'Risk', 'Privacy', 'Reliability']
      .includes(category);
  }

  restoreOriginalAnalysis() {
    const original = this.originalResult();
    if (!original) return;
    this.updateAnalysisResult(original);
    this.analysisEditMode.set(false);
  }

  private updateAnalysisResult(updated: DigestResult) {
    const current = this.result();
    if (!current) return;
    this.result.set(updated);
    this.persistHistory(this.history().map(entry =>
      entry.result === current ? { ...entry, result: updated } : entry));
  }

  selectPresentationFile(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.presentationFile.set(file);
  }

  async generatePresentation() {
    if (!this.requireCloudConsent(() => { void this.generatePresentation(); })) return;

    this.presentationLoading.set(true);
    this.presentationError.set('');
    try {
      const form = new FormData();
      form.append('sourceType', this.presentationSource());
      form.append(
        'sourceReference',
        this.presentationSource() === 'AnalysisResult'
          ? this.result()?.documentTitle ?? ''
          : this.presentationSourceReference()
      );
      if (this.presentationSource() === 'AnalysisResult') {
        const analysis = this.result();
        if (!analysis) throw new Error('Analyze a document before using its results for a presentation.');
        form.append('analysisContext', JSON.stringify({
          documentTitle: analysis.documentTitle,
          executiveSummary: analysis.executiveSummary,
          report: analysis.aiReport
            ? {
                executiveSummary: analysis.aiReport.executiveSummary,
                documentUnderstanding: analysis.aiReport.documentUnderstanding,
                sections: analysis.aiReport.sections ?? [],
                securityAssessment: analysis.aiReport.securityAssessment,
                architectureAssessment: analysis.aiReport.architectureAssessment,
                complianceAssessment: analysis.aiReport.complianceAssessment,
                businessImpact: analysis.aiReport.businessImpact,
                findings: analysis.aiReport.findings.slice(0, 10),
                priorityActionPlan: analysis.aiReport.priorityActionPlan.slice(0, 10),
                overallAssessment: analysis.aiReport.overallAssessment
              }
            : null,
          keyFindings: analysis.keyFindings.slice(0, 10),
          recommendations: analysis.recommendations.slice(0, 10)
        }));
      }
      form.append('useCase', this.presentationUseCase());
      form.append('title', this.presentationTitle());
      form.append('targetAudience', this.presentationAudience());
      form.append('purpose', this.presentationPurpose());
      form.append('slideCount', String(this.presentationSlideCount()));
      form.append('language', this.presentationLanguage());
      form.append('aspectRatio', this.presentationAspectRatio());
      form.append('presentationMode', this.presentationMode());
      form.append('duration', this.presentationDuration());
      form.append('presentationStyle', this.presentationStyle());
      form.append('contentDepth', this.presentationContentDepth());
      form.append('theme', this.presentationTheme());
      form.append('userInstructions', this.presentationInstructions());
      form.append('visualIntensity', this.presentationVisualIntensity());
      form.append('includeSpeakerNotes', String(this.includeSpeakerNotes()));
      form.append('includeArchitectureDiagrams', String(this.includeArchitectureDiagrams()));
      form.append('includeScreenshots', String(this.includeScreenshots()));
      form.append('includeFlowcharts', String(this.includeFlowcharts()));
      form.append('includeDataFlowDiagrams', String(this.includeDataFlowDiagrams()));
      form.append('includeCharts', String(this.includeCharts()));
      form.append('includeProblemSolution', String(this.includeProblemSolution()));
      form.append('includeTechnicalDetails', String(this.includeTechnicalDetails()));
      form.append('includeBusinessRecommendations', String(this.includeBusinessRecommendations()));
      form.append('includeRoadmap', String(this.includeRoadmap()));
      form.append('cloudConsentScope', this.cloudConsentStatus()?.scope ?? '');
      form.append('cloudConsentPolicyVersion', this.cloudConsentStatus()?.policyVersion ?? '');
      if (this.presentationFile()) form.append('file', this.presentationFile()!);
      const result = await this.http.post<PresentationDocument>(
        `${this.apiBaseUrl}/api/presentations/preview`,
        form,
        { withCredentials: true }
      ).toPromise();
      if (!result) throw new Error('The presentation service returned no result.');
      this.presentation.set(result);
      this.presentationTheme.set(result.theme);
      this.presentationOriginal.set(structuredClone(result));
      this.presentationUndoStack.set([]);
      this.presentationRedoStack.set([]);
      this.presentationDirty.set(false);
      this.presentationSaveError.set('');
      this.presentationPreviewIndex.set(0);
      this.selectedPresentationSlides.set(result.slides[0] ? [result.slides[0].sectionId ?? ''] : []);
      this.aiEditProposal.set(null);
      this.aiEditError.set('');
      await this.loadPresentationHistory();
      await this.loadPresentationVersions(result.id);
    } catch (error: unknown) {
      const httpError = error as { status?: number; error?: { code?: string; error?: string; detail?: string } };
      if (httpError.error?.code === 'AI_CLOUD_CONSENT_REQUIRED') {
        this.presentationError.set('');
        this.pendingCloudConsentAction = () => { void this.generatePresentation(); };
        this.cloudConsentChecked.set(false);
        this.cloudConsentDialogOpen.set(true);
        return;
      }
      this.presentationError.set(httpError.error?.error
        ? `${httpError.error.error}${httpError.error.detail ? ` ${httpError.error.detail}` : ''}`
        : httpError.status === 503
          ? 'AI presentation generation is temporarily unavailable. Please wait briefly and retry.'
          : `Presentation generation failed${httpError.status ? ` (${httpError.status})` : ''}.`);
    } finally {
      this.presentationLoading.set(false);
    }
  }

  async loadPresentationHistory() {
    try {
      const history = await this.http.get<PresentationDocument[]>(
        `${this.apiBaseUrl}/api/presentations`).toPromise();
      this.presentationHistory.set(history ?? []);
    } catch {
      this.presentationHistory.set([]);
    }
  }

  previousPresentationSlide() {
    this.presentationPreviewIndex.update(index => Math.max(0, index - 1));
    this.selectCurrentPresentationSlide();
  }

  nextPresentationSlide() {
    const result = this.presentation();
    if (!result) return;
    this.presentationPreviewIndex.update(index => Math.min(result.slides.length - 1, index + 1));
    this.selectCurrentPresentationSlide();
  }

  updatePresentationSlideTitle(value: string) {
    const result = this.presentation();
    if (!result) return;
    const slides = result.slides.map((slide, index) =>
      index === this.presentationPreviewIndex() ? { ...slide, title: value } : slide);
    this.commitPresentationEdit({ ...result, slides });
  }

  updatePresentationSlideSubtitle(value: string) {
    const result = this.presentation();
    if (!result) return;
    const slides = result.slides.map((slide, index) =>
      index === this.presentationPreviewIndex() ? { ...slide, subtitle: value } : slide);
    this.commitPresentationEdit({ ...result, slides });
  }

  updatePresentationSlideNotes(value: string) {
    const result = this.presentation();
    if (!result) return;
    const slides = result.slides.map((slide, index) =>
      index === this.presentationPreviewIndex() ? { ...slide, speakerNotes: value } : slide);
    this.commitPresentationEdit({ ...result, slides });
  }

  updatePresentationSlideContent(value: string, contentIndex: number) {
    const result = this.presentation();
    if (!result) return;
    const slides = result.slides.map((slide, index) => {
      if (index !== this.presentationPreviewIndex()) return slide;
      const content = [...slide.content];
      content[contentIndex] = value;
      return { ...slide, content };
    });
    this.commitPresentationEdit({ ...result, slides });
  }

  addPresentationSlidePoint() {
    const result = this.presentation();
    if (!result || result.slides[this.presentationPreviewIndex()]?.content.length >= 5) return;
    const slides = result.slides.map((slide, index) => index === this.presentationPreviewIndex()
      ? { ...slide, content: [...slide.content, 'New point'] }
      : slide);
    this.commitPresentationEdit({ ...result, slides });
  }

  removePresentationSlidePoint(contentIndex: number) {
    const result = this.presentation();
    if (!result) return;
    const slides = result.slides.map((slide, index) => index === this.presentationPreviewIndex()
      ? { ...slide, content: slide.content.filter((_, itemIndex) => itemIndex !== contentIndex) }
      : slide);
    this.commitPresentationEdit({ ...result, slides });
  }

  addPresentationSlide() {
    const result = this.presentation();
    if (!result || result.slides.length >= 30) return;
    const slides = this.reindexSlides([
      ...result.slides,
      {
        sectionId: globalThis.crypto.randomUUID(),
        slideNumber: result.slides.length + 1,
        title: 'New section',
        subtitle: '',
        layoutType: 'FeatureShowcase',
        content: [],
        visualElements: [],
        screenshotReferences: [],
        speakerNotes: '',
        sourceReferences: [],
        fontFamily: 'Aptos',
        backgroundColor: '',
        textColor: '',
        accentColor: '',
        contentScale: 1,
        contentAlignment: 'Left',
        contentSpacing: 14
      }
    ]);
    this.commitPresentationEdit({ ...result, slides, totalSlides: slides.length });
    this.presentationPreviewIndex.set(slides.length - 1);
  }

  removePresentationSlide() {
    const result = this.presentation();
    if (!result || result.slides.length <= 1) return;
    const slides = this.reindexSlides(result.slides.filter(
      (_, index) => index !== this.presentationPreviewIndex()));
    this.commitPresentationEdit({ ...result, slides, totalSlides: slides.length });
    this.presentationPreviewIndex.set(Math.min(this.presentationPreviewIndex(), slides.length - 1));
  }

  duplicatePresentationSlide() {
    const result = this.presentation();
    if (!result || result.slides.length >= 30) return;
    const index = this.presentationPreviewIndex();
    const source = result.slides[index];
    if (!source) return;
    const clone = structuredClone(source);
    clone.sectionId = globalThis.crypto.randomUUID();
    clone.title = `${source.title} (copy)`;
    const slides = [...result.slides];
    slides.splice(index + 1, 0, clone);
    const reindexed = this.reindexSlides(slides);
    this.commitPresentationEdit({ ...result, slides: reindexed, totalSlides: reindexed.length });
    this.presentationPreviewIndex.set(index + 1);
    this.selectCurrentPresentationSlide();
  }

  movePresentationSlide(direction: -1 | 1) {
    const result = this.presentation();
    if (!result) return;
    const from = this.presentationPreviewIndex();
    const to = from + direction;
    if (to < 0 || to >= result.slides.length) return;
    const slides = [...result.slides];
    [slides[from], slides[to]] = [slides[to], slides[from]];
    this.commitPresentationEdit({ ...result, slides: this.reindexSlides(slides) });
    this.presentationPreviewIndex.set(to);
    this.selectCurrentPresentationSlide();
  }

  startPresentationSlideDrag(index: number) {
    this.draggedPresentationSlide.set(index);
  }

  dropPresentationSlide(index: number) {
    const result = this.presentation();
    const from = this.draggedPresentationSlide();
    this.draggedPresentationSlide.set(null);
    if (!result || from === null || from === index || from < 0 || from >= result.slides.length) return;
    const slides = [...result.slides];
    const [slide] = slides.splice(from, 1);
    slides.splice(index, 0, slide);
    this.commitPresentationEdit({ ...result, slides: this.reindexSlides(slides) });
    this.presentationPreviewIndex.set(index);
    this.selectCurrentPresentationSlide();
  }

  setPresentationZoom(value: number) {
    this.presentationZoom.set(Math.min(1.25, Math.max(0.75, value)));
  }

  selectPresentationSlide(index: number) {
    const deck = this.presentation();
    if (!deck || index < 0 || index >= deck.slides.length) return;
    this.presentationPreviewIndex.set(index);
    if (this.aiEditScope() === 'SelectedSlide' || this.aiEditScope() === 'SelectedElement') {
      const id = deck.slides[index].sectionId;
      if (id) this.selectedPresentationSlides.set([id]);
    }
  }

  togglePresentationSlideSelection(sectionId: string, checked: boolean) {
    this.selectedPresentationSlides.update(selected => checked
      ? [...new Set([...selected, sectionId])]
      : selected.filter(id => id !== sectionId));
  }

  setPresentationEditScope(scope: 'SelectedElement' | 'SelectedSlide' | 'SelectedSlides' | 'EntirePresentation') {
    this.aiEditScope.set(scope);
    if (scope === 'SelectedSlide' || scope === 'SelectedElement') this.selectCurrentPresentationSlide();
    if (scope === 'EntirePresentation') {
      this.selectedPresentationSlides.set(this.presentation()?.slides
        .map(slide => slide.sectionId ?? '')
        .filter(Boolean) ?? []);
    }
  }

  async proposeAiPresentationEdit() {
    if (!this.requireCloudConsent(() => { void this.proposeAiPresentationEdit(); })) return;
    const deck = this.presentation();
    const instruction = this.aiEditInstruction().trim();
    if (!deck || !instruction) {
      this.aiEditError.set('Enter an instruction before asking AI to edit.');
      return;
    }
    const scope = this.aiEditScope();
    const selectedSlide = deck.slides[this.presentationPreviewIndex()];
    const sectionIds = scope === 'EntirePresentation'
      ? deck.slides.map(slide => slide.sectionId ?? '').filter(Boolean)
      : scope === 'SelectedElement' || scope === 'SelectedSlide'
        ? selectedSlide?.sectionId ? [selectedSlide.sectionId] : []
        : this.selectedPresentationSlides();
    if (!sectionIds.length) {
      this.aiEditError.set('Select at least one slide to edit.');
      return;
    }

    this.aiEditLoading.set(true);
    this.aiEditError.set('');
    this.aiEditProposal.set(null);
    const before = structuredClone(deck);
    try {
      const response = await this.http.post<PresentationEditProposal>(
        `${this.apiBaseUrl}/api/presentations/${deck.id}/edit`,
        {
          instruction,
          scope,
          sectionIds,
          element: scope === 'SelectedElement' ? this.aiEditElement() : null,
          cloudConsentScope: this.cloudConsentStatus()?.scope ?? '',
          cloudConsentPolicyVersion: this.cloudConsentStatus()?.policyVersion ?? '',
          presentation: deck
        },
        { withCredentials: true }
      ).toPromise();
      if (!response) throw new Error('The AI editor returned no proposal.');
      this.aiEditProposal.set({
        summary: response.summary,
        provider: response.provider,
        model: response.model,
        before,
        after: response.presentation,
        scope,
        element: this.aiEditElement(),
        sectionIds
      });
    } catch (error: unknown) {
      const httpError = error as { status?: number; error?: { code?: string; error?: string; detail?: string } };
      if (httpError.error?.code === 'AI_CLOUD_CONSENT_REQUIRED') {
        this.pendingCloudConsentAction = () => { void this.proposeAiPresentationEdit(); };
        this.cloudConsentChecked.set(false);
        this.cloudConsentDialogOpen.set(true);
      } else {
        this.aiEditError.set(httpError.error?.error
          ? `${httpError.error.error}${httpError.error.detail ? ` ${httpError.error.detail}` : ''}`
          : `AI editing failed${httpError.status ? ` (HTTP ${httpError.status})` : ''}. Your presentation was not changed.`);
      }
    } finally {
      this.aiEditLoading.set(false);
    }
  }

  acceptAiPresentationEdit() {
    const proposal = this.aiEditProposal();
    const current = this.presentation();
    if (!proposal || !current) return;
    const slides = current.slides.map(slide => {
      if (!proposal.sectionIds.includes(slide.sectionId ?? '')) return slide;
      const proposed = proposal.after.slides.find(candidate => candidate.sectionId === slide.sectionId);
      if (!proposed) return slide;
      if (proposal.scope === 'SelectedElement') {
        return this.applyPresentationElement(slide, proposed, proposal.element);
      }
      return {
        ...slide,
        title: proposed.title,
        subtitle: proposed.subtitle,
        layoutType: proposed.layoutType,
        content: proposed.content,
        visualElements: proposed.visualElements,
        diagram: proposed.diagram,
        chart: proposed.chart,
        speakerNotes: proposed.speakerNotes
      };
    });
    const wholePresentation = proposal.scope === 'EntirePresentation';
    this.commitPresentationEdit({
      ...current,
      ...(wholePresentation ? {
        title: proposal.after.title,
        targetAudience: proposal.after.targetAudience,
        theme: proposal.after.theme
      } : {}),
      slides,
      totalSlides: slides.length
    });
    this.presentationTheme.set(wholePresentation ? proposal.after.theme : current.theme);
    this.aiEditProposal.set(null);
    this.aiEditInstruction.set('');
  }

  rejectAiPresentationEdit() {
    this.aiEditProposal.set(null);
  }

  private applyPresentationElement(
    current: PresentationSlide,
    proposed: PresentationSlide,
    element: string): PresentationSlide {
    switch (element) {
      case 'Title': return { ...current, title: proposed.title };
      case 'Subtitle': return { ...current, subtitle: proposed.subtitle };
      case 'Content': return { ...current, content: proposed.content };
      case 'LayoutType': return { ...current, layoutType: proposed.layoutType };
      case 'VisualElements': return { ...current, visualElements: proposed.visualElements };
      case 'Diagram': return { ...current, diagram: proposed.diagram };
      case 'Chart': return { ...current, chart: proposed.chart };
      case 'SpeakerNotes': return { ...current, speakerNotes: proposed.speakerNotes };
      default: return current;
    }
  }

  updatePresentationSlideLayout(layoutType: string) {
    this.updateCurrentPresentationSlide(slide => ({ ...slide, layoutType }));
  }

  updatePresentationSlideDesign(
    property: 'fontFamily' | 'backgroundColor' | 'textColor' | 'accentColor' | 'contentScale' | 'contentAlignment' | 'contentSpacing',
    value: string | number) {
    this.updateCurrentPresentationSlide(slide => ({ ...slide, [property]: value }));
  }

  addPresentationVisualElement() {
    this.updateCurrentPresentationSlide(slide => ({
      ...slide,
      visualElements: slide.visualElements.length >= 6
        ? slide.visualElements
        : [...slide.visualElements, { type: 'Card', label: 'New visual', value: 'Describe it' }]
    }));
  }

  updatePresentationVisualElement(index: number, property: 'type' | 'label' | 'value', value: string) {
    this.updateCurrentPresentationSlide(slide => ({
      ...slide,
      visualElements: slide.visualElements.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [property]: value } : item)
    }));
  }

  removePresentationVisualElement(index: number) {
    this.updateCurrentPresentationSlide(slide => ({
      ...slide,
      visualElements: slide.visualElements.filter((_, itemIndex) => itemIndex !== index)
    }));
  }

  async addPresentationImage(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp', 'image/gif'].includes(file.type) || file.size > 1_000_000) {
      this.presentationSaveError.set('Choose a PNG, JPEG, WebP or GIF image no larger than 1 MB.');
      return;
    }
    const reader = new FileReader();
    reader.onerror = () => this.presentationSaveError.set('The selected image could not be read.');
    reader.onload = () => {
      if (typeof reader.result !== 'string') {
        this.presentationSaveError.set('The selected image could not be converted for offline export.');
        return;
      }
      this.updateCurrentPresentationSlide(slide => ({ ...slide, imageDataUrl: reader.result as string }));
    };
    reader.readAsDataURL(file);
    (event.target as HTMLInputElement).value = '';
  }

  removePresentationImage() {
    this.updateCurrentPresentationSlide(slide => ({ ...slide, imageDataUrl: undefined }));
  }

  updatePresentationDiagram(mermaid: string) {
    this.updateCurrentPresentationSlide(slide => ({
      ...slide,
      diagram: mermaid.trim() ? { type: 'Workflow', mermaid } : undefined
    }));
  }

  presentationChartJson(slide: PresentationSlide) {
    return slide.chart ? JSON.stringify(slide.chart, null, 2) : '';
  }

  updatePresentationChartJson(value: string) {
    if (!value.trim()) {
      this.updateCurrentPresentationSlide(slide => ({ ...slide, chart: undefined }));
      return;
    }
    try {
      const parsed: unknown = JSON.parse(value);
      if (typeof parsed !== 'object' || parsed === null) throw new Error();
      const candidate = parsed as { type?: unknown; labels?: unknown; values?: unknown };
      const chartType = candidate.type;
      if (typeof chartType !== 'string' ||
          !Array.isArray(candidate.labels) ||
          !candidate.labels.every(label => typeof label === 'string') ||
          !Array.isArray(candidate.values) ||
          !candidate.values.every(value => typeof value === 'number' && Number.isFinite(value)) ||
          candidate.labels.length !== candidate.values.length ||
          candidate.labels.length > 20) {
        throw new Error();
      }
      this.updateCurrentPresentationSlide(slide => ({
        ...slide,
        chart: { type: chartType, labels: candidate.labels as string[], values: candidate.values as number[] }
      }));
      this.presentationSaveError.set('');
    } catch {
      this.presentationSaveError.set('Chart data must be valid JSON with matching string labels and numeric values.');
    }
  }

  presentationTableJson(slide: PresentationSlide) {
    return slide.table ? JSON.stringify(slide.table, null, 2) : '';
  }

  updatePresentationTableJson(value: string) {
    if (!value.trim()) {
      this.updateCurrentPresentationSlide(slide => ({ ...slide, table: undefined }));
      return;
    }
    try {
      const parsed: unknown = JSON.parse(value);
      if (typeof parsed !== 'object' || parsed === null) throw new Error();
      const candidate = parsed as { headers?: unknown; rows?: unknown };
      if (!Array.isArray(candidate.headers) ||
          candidate.headers.length < 1 || candidate.headers.length > 8 ||
          !candidate.headers.every(header => typeof header === 'string' && header.length <= 120) ||
          !Array.isArray(candidate.rows) || candidate.rows.length > 20) throw new Error();
      const headers = candidate.headers;
      if (!candidate.rows.every(row => Array.isArray(row) &&
          row.length === headers.length &&
          row.every(cell => typeof cell === 'string' && cell.length <= 300))) throw new Error();
      this.updateCurrentPresentationSlide(slide => ({
        ...slide,
        table: { headers: headers as string[], rows: candidate.rows as string[][] }
      }));
      this.presentationSaveError.set('');
    } catch {
      this.presentationSaveError.set('Table data must be valid JSON with 1–8 headers and matching rows. Headers are limited to 120 characters and cells to 300.');
    }
  }

  addPresentationTableTemplate() {
    this.updateCurrentPresentationSlide(slide => ({
      ...slide,
      table: { headers: ['Item', 'Details'], rows: [['New item', 'Add details']] }
    }));
  }

  async loadPresentationVersions(id: string) {
    try {
      const versions = await this.http.get<PresentationVersionSummary[]>(
        `${this.apiBaseUrl}/api/presentations/${id}/versions`).toPromise();
      this.presentationVersions.set(versions ?? []);
      const original = versions?.find(version => version.label === 'Original AI draft');
      if (original && !this.presentationOriginal()) {
        const draft = await this.http.get<PresentationDocument>(
          `${this.apiBaseUrl}/api/presentations/${id}/versions/${original.id}`).toPromise();
        if (draft) this.presentationOriginal.set(draft);
      }
    } catch {
      this.presentationVersions.set([]);
    }
  }

  async restorePresentationVersion(version: PresentationVersionSummary) {
    const deck = this.presentation();
    if (!deck) return;
    try {
      const restored = await this.http.post<PresentationDocument>(
        `${this.apiBaseUrl}/api/presentations/${deck.id}/versions/${version.id}/restore`,
        {},
        { withCredentials: true }
      ).toPromise();
      if (!restored) throw new Error('The version service returned no presentation.');
      const current = this.presentation();
      if (current) this.presentationUndoStack.update(stack => [...stack, current].slice(-30));
      this.presentationRedoStack.set([]);
      this.presentation.set(restored);
      this.presentationTheme.set(restored.theme);
      this.presentationDirty.set(false);
      if (this.presentationAutosaveTimer) clearTimeout(this.presentationAutosaveTimer);
      this.presentationAutosaveTimer = null;
      this.presentationPreviewIndex.set(Math.min(this.presentationPreviewIndex(), restored.slides.length - 1));
      await this.loadPresentationVersions(deck.id);
    } catch {
      this.presentationSaveError.set('Could not restore this presentation version. Current edits remain available.');
    }
  }

  restoreOriginalPresentation() {
    const original = this.presentationOriginal();
    if (!original) return;
    this.commitPresentationEdit(structuredClone(original));
    this.presentationPreviewIndex.set(0);
    this.presentationTheme.set(original.theme);
  }

  async comparePresentationVersion(version: PresentationVersionSummary) {
    const deck = this.presentation();
    if (!deck) return;
    try {
      const snapshot = await this.http.get<PresentationDocument>(
        `${this.apiBaseUrl}/api/presentations/${deck.id}/versions/${version.id}`).toPromise();
      if (snapshot) this.presentationCompareVersion.set({ ...version, presentation: snapshot });
    } catch {
      this.presentationSaveError.set('Could not load the selected version for comparison.');
    }
  }

  private updateCurrentPresentationSlide(
    update: (slide: PresentationSlide) => PresentationSlide) {
    const deck = this.presentation();
    if (!deck) return;
    const index = this.presentationPreviewIndex();
    const slides = deck.slides.map((slide, slideIndex) => slideIndex === index ? update(slide) : slide);
    this.commitPresentationEdit({ ...deck, slides });
  }

  private selectCurrentPresentationSlide() {
    const slide = this.presentation()?.slides[this.presentationPreviewIndex()];
    if (slide?.sectionId &&
        (this.aiEditScope() === 'SelectedSlide' || this.aiEditScope() === 'SelectedElement')) {
      this.selectedPresentationSlides.set([slide.sectionId]);
    }
  }

  private schedulePresentationAutosave() {
    if (this.presentationAutosaveTimer) clearTimeout(this.presentationAutosaveTimer);
    this.presentationAutosaveTimer = setTimeout(() => {
      this.presentationAutosaveTimer = null;
      void this.savePresentationEdits();
    }, 900);
  }

  undoPresentationEdit() {
    const previous = this.presentationUndoStack().at(-1);
    const current = this.presentation();
    if (!previous || !current) return;
    this.presentationUndoStack.update(stack => stack.slice(0, -1));
    this.presentationRedoStack.update(stack => [...stack, current].slice(-30));
    this.presentation.set(previous);
    this.presentationDirty.set(true);
    this.aiEditProposal.set(null);
    this.schedulePresentationAutosave();
    this.presentationPreviewIndex.set(Math.min(this.presentationPreviewIndex(), previous.slides.length - 1));
  }

  redoPresentationEdit() {
    const next = this.presentationRedoStack().at(-1);
    const current = this.presentation();
    if (!next || !current) return;
    this.presentationRedoStack.update(stack => stack.slice(0, -1));
    this.presentationUndoStack.update(stack => [...stack, current].slice(-30));
    this.presentation.set(next);
    this.presentationDirty.set(true);
    this.aiEditProposal.set(null);
    this.schedulePresentationAutosave();
    this.presentationPreviewIndex.set(Math.min(this.presentationPreviewIndex(), next.slides.length - 1));
  }

  selectPresentationTheme(theme: string) {
    this.presentationTheme.set(theme);
    const result = this.presentation();
    if (result) this.commitPresentationEdit({ ...result, theme });
  }

  presentationThemeClass(theme: string | undefined) {
    const key = (theme ?? '').toLowerCase();
    if (key.includes('prism')) return 'theme-prism';
    if (key.includes('neural')) return 'theme-neural';
    if (key.includes('cinematic')) return 'theme-cinematic';
    if (key.includes('gold')) return 'theme-gold';
    if (key.includes('blueprint')) return 'theme-blueprint';
    if (key.includes('universe')) return 'theme-universe';
    if (key.includes('storytelling')) return 'theme-story';
    if (key.includes('dark')) return 'theme-neural';
    if (key.includes('technology')) return 'theme-blueprint';
    return 'theme-default';
  }

  presentationLayoutClass(layout: string | undefined) {
    const key = (layout ?? '').toLowerCase();
    if (['title', 'hero', 'executivesummary'].includes(key)) return 'layout-hero';
    if (['workflow', 'userjourney', 'decisiontree', 'swimlane', 'sequencediagram'].includes(key)) return 'layout-workflow';
    if (['architecturediagram', 'blueprint', 'dataflow', 'integrationarchitecture',
      'securityarchitecture', 'technologystack'].includes(key)) return 'layout-blueprint';
    if (['conclusion', 'qanda'].includes(key)) return 'layout-conclusion';
    if (['kpidashboard', 'dashboard', 'insight'].includes(key)) return 'layout-dashboard';
    if (['comparison', 'comparisonmatrix', 'problem', 'problemandsolution',
      'businessecosystem'].includes(key)) return 'layout-comparison';
    if (['roadmap', 'timeline', 'implementationroadmap'].includes(key)) return 'layout-roadmap';
    if (['riskmitigation', 'riskassessment'].includes(key)) return 'layout-risk';
    if (['capacityallocation', 'allocation'].includes(key)) return 'layout-allocation';
    if (['chart', 'barchart', 'donutchart', 'linechart', 'funnelchart'].includes(key)) return 'layout-chart';
    return 'layout-story';
  }

  isDarkPresentationTheme(theme: string | undefined) {
    return ['Neural Intelligence', 'Executive Gold', 'Digital Blueprint', 'Knowledge Universe',
      'Executive Dark', 'Technology Blue'].includes(theme ?? '');
  }

  private commitPresentationEdit(updated: PresentationDocument) {
    const current = this.presentation();
    if (!current) return;
    this.presentationUndoStack.update(stack => [...stack, current].slice(-30));
    this.presentationRedoStack.set([]);
    this.presentation.set(updated);
    this.presentationDirty.set(true);
    this.presentationSaveError.set('');
    this.aiEditProposal.set(null);
    this.schedulePresentationAutosave();
  }

  private reindexSlides(slides: PresentationSlide[]) {
    return slides.map((slide, index) => ({ ...slide, slideNumber: index + 1 }));
  }

  downloadPresentationPptx(presentation: PresentationDocument) {
    window.open(`${this.apiBaseUrl}/api/presentations/${presentation.id}/pptx`, '_blank');
  }

  downloadPresentationPdf(presentation: PresentationDocument) {
    window.open(`${this.apiBaseUrl}/api/presentations/${presentation.id}/pdf`, '_blank');
  }

  downloadPresentationHtml(presentation: PresentationDocument) {
    window.open(`${this.apiBaseUrl}/api/presentations/${presentation.id}/html`, '_blank');
  }

  openPresentation(presentation: PresentationDocument) {
    this.presentation.set(presentation);
    this.presentationTheme.set(presentation.theme);
    this.presentationOriginal.set(null);
    this.presentationUndoStack.set([]);
    this.presentationRedoStack.set([]);
    this.presentationDirty.set(false);
    this.presentationSaveError.set('');
    this.presentationPreviewIndex.set(0);
    this.selectedPresentationSlides.set(presentation.slides[0] ? [presentation.slides[0].sectionId ?? ''] : []);
    this.aiEditProposal.set(null);
    this.aiEditError.set('');
    void this.loadPresentationVersions(presentation.id);
  }

  startPresentation(presentation?: PresentationDocument) {
    if (presentation) this.openPresentation(presentation);
    if (!this.presentation()) return;
    this.presentationViewerOpen.set(true);
    this.presentationOverviewOpen.set(false);
    this.presentationNotesOpen.set(false);
    this.presentationElapsedSeconds.set(0);
    this.presentationRemainingSeconds.set(this.durationSeconds());
  }

  closePresentation() {
    this.presentationViewerOpen.set(false);
    this.presentationOverviewOpen.set(false);
    this.presentationNotesOpen.set(false);
    this.stopPresentationTimer();
  }

  togglePresentationFullscreen() {
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else {
      void document.documentElement.requestFullscreen();
    }
  }

  togglePresentationTimer() {
    if (this.presentationTimerRunning()) {
      this.stopPresentationTimer();
      return;
    }
    this.presentationTimerRunning.set(true);
    this.presentationTimer = setInterval(() => {
      if (!this.presentationTimerRunning()) return;
      this.presentationElapsedSeconds.update(value => value + 1);
      this.presentationRemainingSeconds.update(value => Math.max(0, value - 1));
    }, 1000);
  }

  stopPresentationTimer() {
    if (this.presentationTimer) clearInterval(this.presentationTimer);
    this.presentationTimer = null;
    this.presentationTimerRunning.set(false);
  }

  resetPresentationTimer() {
    this.stopPresentationTimer();
    this.presentationElapsedSeconds.set(0);
    this.presentationRemainingSeconds.set(this.durationSeconds());
  }

  durationSeconds() {
    const match = this.presentationDuration().match(/(\d+)/);
    return (match ? Number(match[1]) : 15) * 60;
  }

  formatPresentationTime(seconds: number) {
    const minutes = Math.floor(seconds / 60);
    const remaining = seconds % 60;
    return `${minutes}:${remaining.toString().padStart(2, '0')}`;
  }

  selectPresentationOverviewIndex(index: number) {
    this.presentationPreviewIndex.set(index);
    this.presentationOverviewOpen.set(false);
  }

  setPresentationViewerMode(mode: 'Linear' | 'Scroll') {
    this.presentationViewerMode.set(mode);
  }

  togglePresentationTheme() {
    const themes = [
      'Prism Blue',
      'Neural Intelligence',
      'Cinematic Product Launch',
      'Executive Gold',
      'Digital Blueprint',
      'Immersive Storytelling',
      'Knowledge Universe'
    ];
    const current = this.presentation()?.theme ?? this.presentationTheme();
    const nextIndex = (themes.indexOf(current) + 1) % themes.length;
    const next = themes[nextIndex];
    this.selectPresentationTheme(next);
  }

  async savePresentationEdits() {
    if (this.presentationSaveInProgress) return;
    const current = this.presentation();
    if (!current || !this.presentationDirty()) return;
    this.presentationSaveInProgress = true;
    this.presentationSaving.set(true);
    this.presentationSaveError.set('');
    try {
      const saved = await this.http.put<PresentationDocument>(
        `${this.apiBaseUrl}/api/presentations/${current.id}`,
        current,
        { withCredentials: true }
      ).toPromise();
      if (!saved) throw new Error('The presentation service returned no saved version.');
      const latest = this.presentation();
      if (latest && JSON.stringify(latest) === JSON.stringify(current)) {
        this.presentation.set(saved);
        this.presentationDirty.set(false);
      }
      await this.loadPresentationHistory();
      await this.loadPresentationVersions(current.id);
    } catch (error: unknown) {
      const status = (error as { status?: number }).status;
      this.presentationSaveError.set(
        `Could not save presentation edits${status ? ` (HTTP ${status})` : ''}. Your local edits remain available.`);
    } finally {
      this.presentationSaveInProgress = false;
      this.presentationSaving.set(false);
      const latest = this.presentation();
      if (latest && JSON.stringify(latest) !== JSON.stringify(current)) this.schedulePresentationAutosave();
    }
  }

  @HostListener('document:keydown', ['$event'])
  handlePresentationKeyboard(event: KeyboardEvent) {
    if (!this.presentationViewerOpen()) return;
    if (event.key === 'ArrowRight' || event.key === ' ') {
      event.preventDefault();
      this.nextPresentationSlide();
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.previousPresentationSlide();
    } else if (event.key.toLowerCase() === 'f') {
      this.togglePresentationFullscreen();
    } else if (event.key === 'Escape') {
      this.closePresentation();
    } else if (event.key === 'Home') {
      this.presentationPreviewIndex.set(0);
    } else if (event.key === 'End') {
      const deck = this.presentation();
      if (deck) this.presentationPreviewIndex.set(deck.slides.length - 1);
    } else if (event.key.toLowerCase() === 'o') {
      this.presentationOverviewOpen.update(open => !open);
    } else if (event.key.toLowerCase() === 'n') {
      this.presentationNotesOpen.update(open => !open);
    }
  }

  async deletePresentation(presentation: PresentationDocument) {
    await this.http.delete(`${this.apiBaseUrl}/api/presentations/${presentation.id}`).toPromise();
    await this.loadPresentationHistory();
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
    if (!entry.result) return 'ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â';
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

  startAnalysisTimer() {
    this.stopAnalysisTimer();
    this.analysisTimer = setInterval(() => {
      this.analysisElapsedSeconds.update(seconds => seconds + 1);
    }, 1000);
  }

  stopAnalysisTimer() {
    if (this.analysisTimer) clearInterval(this.analysisTimer);
    this.analysisTimer = null;
  }

  cancelAnalysis() {
    this.analysisSubscription?.unsubscribe();
    this.analysisSubscription = null;
    this.closeAnalysisProgress();
    this.stopAnalysisTimer();
    this.loading.set(false);
    this.apiError.set('Analysis cancelled. Provider requests already accepted may still consume quota.');
  }

  formatAnalysisElapsed() {
    const seconds = this.analysisElapsedSeconds();
    const minutes = Math.floor(seconds / 60);
    return `${minutes}:${(seconds % 60).toString().padStart(2, '0')}`;
  }

  private sendAnalysis(
    request: Observable<DigestResult>,
    title: string,
    type: string,
    size: number
  ) {
    this.analysisSubscription?.unsubscribe();
    this.analysisSubscription = request.subscribe({
      next: value => this.setResult(value, title, type, size),
      error: error => {
        this.closeAnalysisProgress();
        const payload = error?.error as { code?: unknown; error?: unknown } | undefined;
        if (payload?.code === 'AI_CLOUD_CONSENT_REQUIRED') {
          this.stopAnalysisTimer();
          this.loading.set(false);
          this.pendingCloudConsentAction = () => this.analyse();
          this.cloudConsentChecked.set(false);
          this.cloudConsentDialogOpen.set(true);
          return;
        }
        const apiMessage = typeof payload?.error === 'string' ? payload.error : '';
        const message = apiMessage || (error.status === 0
          ? 'Analysis API is unreachable. Start the API and try again.'
          : error.status === 401
            ? 'Sign in is required to analyze documents.'
            : error.status === 403
              ? 'Cloud processing needs your consent.'
              : error.status === 413
                ? 'The document exceeds the configured analysis size limit.'
                : error.status === 429
                  ? 'Daily AI usage limit reached. Please try again later.'
                  : error.status === 504
                    ? 'Analysis request timed out. Please try again.'
                    : error.status === 503
                        ? apiMessage
                          ? `AI analysis unavailable: ${apiMessage}`
                          : 'AI analysis is temporarily unavailable. Please try again.'
                      : `Analysis failed (${error.status || 'unknown error'}).`);
        this.apiError.set(message);
        this.recordFailure(title, type, size, message);
        this.stopAnalysisTimer();
        this.loading.set(false);
      },
      complete: () => {
        this.closeAnalysisProgress();
        this.stopAnalysisTimer();
        this.loading.set(false);
      }
    });
  }

  private openAnalysisProgress(progressId: string) {
    this.closeAnalysisProgress();
    const stream = new EventSource(
      `${this.apiBaseUrl}/api/documents/analysis-progress/${progressId}`,
      { withCredentials: true }
    );
    this.analysisProgressStream = stream;
    stream.onmessage = event => {
      try {
        const progress = JSON.parse(event.data) as AnalysisProgressEvent;
        this.analysisProgress.set(progress);
        if (!this.analysisStages().includes(progress.stage))
          this.analysisStages.update(stages => [...stages, progress.stage]);
        if (progress.completed || progress.cancelled || progress.failed)
          this.closeAnalysisProgress();
      } catch {
        this.analysisProgressUnavailable.set(true);
        this.closeAnalysisProgress();
      }
    };
    stream.onerror = () => {
      this.analysisProgressUnavailable.set(true);
      this.closeAnalysisProgress();
    };
  }

  private closeAnalysisProgress() {
    this.analysisProgressStream?.close();
    this.analysisProgressStream = null;
  }

  private setResult(value: DigestResult, title: string, type: string, size: number) {
    this.stopAnalysisTimer();
    this.result.set(value);
    this.originalResult.set(value);
    this.analysisEditMode.set(false);
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
      return [
        `# Document Understanding: ${data.documentTitle}`,
        '',
        `**Source type:** ${report.sourceType || data.documentType}`,
        `**Provider / model:** ${model?.provider ?? data.analysisProvider ?? 'Unknown'} / ${model?.modelName ?? 'Unknown'}`,
        `**Model version:** ${model?.modelVersion ?? 'Not reported'}`,
        `**Analysis status:** ${model?.status ?? 'Completed'}`,
        `**Inference calls:** ${model?.inferenceCalls ?? 'Not available'}`,
        '',
        '## Overview',
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
        ...(report.sections ?? []).flatMap(section => [
          `## ${section.title}`,
          `**Type / origin:** ${section.type} / ${section.origin}`,
          section.content,
          ...section.evidence.map(evidence =>
            `> ${evidence.quotation}\n>\n> Source lines ${evidence.startLine}-${evidence.endLine}`),
          ''
        ]),
        ...(report.findings.length ? ['## Source-grounded observations'] : []),
        ...(report.findings.length
          ? report.findings.flatMap(finding => [
              `### ${finding.title}`,
              `**Category/status:** ${finding.category} / ${finding.status}`,
              `**Observation:** ${finding.observation}`,
              ...(finding.recommendation ? [`**Next step:** ${finding.recommendation}`] : []),
              ...finding.evidence.map(evidence =>
                `> ${evidence.quotation}\n>\n> Source lines ${evidence.startLine}-${evidence.endLine}`),
              ''
            ])
          : ['No evidence-backed findings were identified.', '']),
        ...[
          ['Security insights', report.securityAssessment],
          ['Architecture insights', report.architectureAssessment],
          ['Compliance insights', report.complianceAssessment],
          ['Business impact', report.businessImpact]
        ].flatMap(([title, content]) => content ? [`## ${title}`, content, ''] : []),
        '',
        ...(report.priorityActionPlan.length ? ['## Next steps'] : []),
        ...(report.priorityActionPlan.length
          ? report.priorityActionPlan.map((action, index) => `${index + 1}. ${action}`)
          : []),
        '',
        '## Source review',
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
        `## ${finding.priority}: ${finding.title}\n\n${finding.description}\n\nAgent: ${finding.agentName} Ãƒâ€šÃ‚Â· Confidence: ${finding.confidence}% Ãƒâ€šÃ‚Â· Source: ${finding.evidence?.section}, line ${finding.evidence?.sourceLineStart}\n\n> ${finding.evidence?.sourceText ?? ''}`)
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
