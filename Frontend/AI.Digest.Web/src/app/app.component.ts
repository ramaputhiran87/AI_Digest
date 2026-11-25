import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

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

interface DigestResult {
  documentTitle: string;
  documentType: string;
  executiveSummary: string;
  highPriorityItems: Finding[];
  criticalRisks: Finding[];
  dependencies: Finding[];
  contradictions: { finding: Finding; evidenceA: Evidence; evidenceB: Evidence }[];
  humanReviewRequired: Finding[];
  recommendations: Finding[];
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

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private http = inject(HttpClient);
  useCase = signal('');
  fileName = signal('');
  selectedFile = signal<File | null>(null);
  loading = signal(false);
  apiError = signal('');
  activeTab = signal('Digest');
  result = signal<DigestResult | null>(null);
  findings = computed(() => {
    const data = this.result();
    if (!data) return [];
    const items = [
      ...data.criticalRisks,
      ...data.highPriorityItems,
      ...data.dependencies,
      ...data.humanReviewRequired,
      ...data.recommendations
    ];
    return items.filter((item, index, list) =>
      list.findIndex(candidate => candidate.id === item.id) === index);
  });

  selectFile(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    const supported = ['.md', '.txt', '.html', '.json', '.xml'];
    if (!supported.includes(file.name.slice(file.name.lastIndexOf('.')).toLowerCase())) {
      alert('Supported formats: Markdown, TXT, HTML, JSON and XML.');
      input.value = '';
      return;
    }
    this.selectedFile.set(file);
    this.fileName.set(file.name);
  }

  analyse() {
    if (!this.selectedFile() && !this.useCase().trim()) return;
    this.loading.set(true);
    this.apiError.set('');
    if (this.selectedFile()) {
      const form = new FormData();
      form.append('file', this.selectedFile()!);
      form.append('useCase', this.useCase());
      this.http.post<DigestResult>('http://127.0.0.1:5050/api/documents/analyse', form)
        .subscribe({
          next: value => this.result.set(value),
          error: (error) => {
            this.result.set(null);
            this.apiError.set(error.status
              ? `Analysis failed (${error.status}). Check that the API is running on port 5050.`
              : 'Analysis API is unavailable.');
          },
          complete: () => this.loading.set(false)
        });
    } else {
      this.http.post<DigestResult>('http://127.0.0.1:5050/api/documents/analyse-text', {
        text: this.useCase(),
        title: 'Use-case.txt',
        useCase: this.useCase()
      }).subscribe({
        next: value => this.result.set(value),
        error: (error) => {
          this.result.set(null);
          this.apiError.set(error.status
            ? `Analysis failed (${error.status}). Check that the API is running on port 5050.`
            : 'Analysis API is unavailable.');
        },
        complete: () => this.loading.set(false)
      });
    }
  }

  setTab(tab: string) { this.activeTab.set(tab); }

  jumpToSource(evidence: Evidence) {
    alert(`Source ${evidence.section}, line ${evidence.sourceLineStart}-${evidence.sourceLineEnd}`);
  }

  exportJson() {
    const data = JSON.stringify(this.result() ?? this.demoResult(), null, 2);
    this.download(data, 'application/json', 'digest.json');
  }

  downloadCompressedMarkdown() {
    const data = this.result();
    if (!data) return;
    this.download(
      data.compressedDocument.markdown,
      'text/markdown',
      data.compressedDocument.fileName
    );
  }

  private download(content: string, type: string, name: string) {
    const url = URL.createObjectURL(new Blob([content], { type }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private demoResult(): DigestResult {
    return {
      documentTitle: this.fileName() || 'Use-case.txt',
      documentType: 'Plain Text',
      executiveSummary: 'No evidence-backed findings could be returned by the API. Human review is required.',
      highPriorityItems: [],
      criticalRisks: [],
      dependencies: [],
      contradictions: [],
      humanReviewRequired: [],
      recommendations: [],
      confidenceScore: 45,
      confidenceBand: 'Weak Evidence',
      evidence: []
      ,
      contradictionDetails: [],
      compressedDocument: {
        fileName: 'Use-case_Compressed.md',
        markdown: '# Compressed Review',
        statistics: {
          originalLines: 0,
          compressedLines: 1,
          removedLines: 0,
          compressionPercent: 0,
          noiseRemovedPercent: 0
        }
      }
    };
  }
}
