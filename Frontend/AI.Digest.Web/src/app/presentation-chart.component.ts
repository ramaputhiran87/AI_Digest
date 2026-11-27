import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-presentation-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <figure class="chart-frame">
      <figcaption>{{ chart?.type || 'Data' }} · Source-provided values</figcaption>
      @if (kind === 'donut') {
        <div class="donut-layout">
          <svg viewBox="0 0 160 160" role="img" aria-label="Donut chart">
            <circle class="donut-track" cx="80" cy="80" r="56"></circle>
            @for (segment of segments; track $index) {
              <circle class="donut-segment" cx="80" cy="80" r="56"
                [attr.stroke]="colors[$index % colors.length]"
                [attr.stroke-dasharray]="segment.dash"
                [attr.stroke-dashoffset]="segment.offset"></circle>
            }
            <text x="80" y="77">{{ total }}</text><text class="donut-caption" x="80" y="96">total</text>
          </svg>
          <ul>@for (item of items; track $index) { <li><i [style.background]="colors[$index % colors.length]"></i><span>{{ item.label }}</span><strong>{{ item.value }}</strong></li> }</ul>
        </div>
      } @else {
        <svg class="axis-chart" viewBox="0 0 720 250" role="img" [attr.aria-label]="kind + ' chart'">
          <line x1="52" y1="206" x2="700" y2="206"></line>
          <line x1="52" y1="20" x2="52" y2="206"></line>
          @if (kind === 'line') {
            <polyline class="line-series" [attr.points]="linePoints"></polyline>
            @for (point of points; track $index) {
              <circle class="line-point" [attr.cx]="point.x" [attr.cy]="point.y" r="5"><title>{{ point.label }}: {{ point.value }}</title></circle>
            }
          } @else {
            @for (point of points; track $index) {
              <rect class="bar-series" [attr.x]="point.x - barWidth / 2" [attr.y]="point.y"
                [attr.width]="barWidth" [attr.height]="206 - point.y" rx="5">
                <title>{{ point.label }}: {{ point.value }}</title>
              </rect>
              <text class="bar-value" [attr.x]="point.x" [attr.y]="point.y - 7">{{ point.value }}</text>
            }
          }
          @for (point of points; track $index) {
            <text class="axis-label" [attr.x]="point.x" y="229">{{ point.label }}</text>
          }
        </svg>
      }
    </figure>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .chart-frame { margin: 0; padding: 14px; border: 1px solid var(--dd-border, #dce7f5); border-radius: 14px; background: rgba(255,255,255,.72); }
    figcaption { margin-bottom: 10px; color: #64748b; font-size: 11px; font-weight: 700; }
    .axis-chart { width: 100%; height: auto; overflow: visible; }
    .axis-chart line { stroke: rgba(100,116,139,.3); stroke-width: 1; }
    .bar-series { fill: #008ff5; }
    .line-series { fill: none; stroke: #008ff5; stroke-width: 4; stroke-linecap: round; stroke-linejoin: round; }
    .line-point { fill: #00cbe8; stroke: #fff; stroke-width: 2; }
    .axis-label, .bar-value { fill: #64748b; font: 11px "Segoe UI", sans-serif; text-anchor: middle; }
    .bar-value { fill: #142b52; font-weight: 700; }
    .donut-layout { display: grid; grid-template-columns: minmax(110px, 160px) minmax(0, 1fr); align-items: center; gap: 18px; }
    .donut-layout svg { width: 100%; transform: rotate(-90deg); }
    .donut-track, .donut-segment { fill: none; stroke-width: 19; }
    .donut-track { stroke: rgba(100,116,139,.14); }
    .donut-segment { stroke-linecap: butt; }
    .donut-layout svg text { fill: #10234d; font: 700 17px "Segoe UI", "Noto Sans Tamil", "Latha", "Nirmala UI", sans-serif; text-anchor: middle; transform: rotate(90deg); transform-origin: 80px 80px; }
    .donut-layout svg .donut-caption { fill: #64748b; font-size: 10px; font-weight: 500; }
    .donut-layout ul { display: grid; gap: 8px; margin: 0; padding: 0; list-style: none; }
    .donut-layout li { display: grid; grid-template-columns: 9px minmax(0,1fr) auto; align-items: center; gap: 8px; color: #64748b; font-size: 11px; }
    .donut-layout li i { width: 8px; height: 8px; border-radius: 50%; }
    .donut-layout li strong { color: #142b52; }
    :host-context(.viewer-dark) .chart-frame { background: rgba(255,255,255,.06); }
    :host-context(.viewer-dark) .donut-layout svg text { fill: #f3f7ff; }
    :host-context(.viewer-dark) .donut-layout li strong { color: #f3f7ff; }
    @media (max-width: 640px) { .donut-layout { grid-template-columns: 110px minmax(0,1fr); gap: 10px; } }
  `]
})
export class PresentationChartComponent {
  @Input() chart?: { type: string; labels: string[]; values: number[] };

  readonly colors = ['#008ff5', '#00cbe8', '#00b5b8', '#ffb547', '#ff5349', '#0756b5'];

  get items() {
    return (this.chart?.labels ?? []).slice(0, 12).map((label, index) => ({
      label,
      value: Number(this.chart?.values[index] ?? 0)
    })).filter(item => Number.isFinite(item.value));
  }

  get kind() {
    const type = (this.chart?.type ?? '').toLowerCase();
    if (type.includes('donut') || type.includes('doughnut') || type.includes('pie')) return 'donut';
    if (type.includes('line') || type.includes('trend')) return 'line';
    return 'bar';
  }

  get total() {
    return this.items.reduce((sum, item) => sum + item.value, 0);
  }

  get barWidth() {
    return Math.min(42, 560 / Math.max(1, this.items.length) * 0.62);
  }

  get points() {
    const max = Math.max(1, ...this.items.map(item => item.value));
    const step = 620 / Math.max(1, this.items.length);
    return this.items.map((item, index) => ({
      ...item,
      x: 65 + step * (index + 0.5),
      y: 198 - Math.max(0, item.value) / max * 165
    }));
  }

  get linePoints() {
    return this.points.map(point => `${point.x},${point.y}`).join(' ');
  }

  get segments() {
    const circumference = 2 * Math.PI * 56;
    let offset = 0;
    return this.items.map(item => {
      const length = this.total > 0 ? circumference * Math.max(0, item.value) / this.total : 0;
      const segment = { dash: `${length} ${circumference - length}`, offset: -offset };
      offset += length;
      return segment;
    });
  }
}
