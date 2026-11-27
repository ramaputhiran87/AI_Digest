import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

interface DiagramNode {
  id: string;
  label: string;
  shape: 'rect' | 'diamond' | 'round' | 'circle';
  rank: number;
  x: number;
  y: number;
}

interface DiagramEdge {
  from: string;
  to: string;
  label: string;
  path: string;
  labelX: number;
  labelY: number;
}

@Component({
  selector: 'app-presentation-diagram',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (nodes.length) {
      <div class="diagram-toolbar">
        <span>Scroll to pan</span>
        <button type="button" aria-label="Zoom out diagram" (click)="adjustZoom(-0.2)">−</button>
        <button type="button" (click)="resetZoom()">Reset</button>
        <button type="button" aria-label="Zoom in diagram" (click)="adjustZoom(0.2)">+</button>
      </div>
      <div class="diagram-scroll">
        <svg class="diagram-svg" [attr.viewBox]="viewBox" [style.width.px]="diagramWidth * zoom"
          [style.height.px]="diagramHeight * zoom" role="img" [attr.aria-label]="ariaLabel">
          <defs>
            <marker [attr.id]="markerId" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto">
              <path d="M0,0 L8,4 L0,8 Z"></path>
            </marker>
          </defs>
          @for (edge of edges; track $index) {
            <path class="diagram-edge" [attr.d]="edge.path" [attr.marker-end]="'url(#' + markerId + ')'"></path>
            @if (edge.label) { <text class="diagram-edge-label" [attr.x]="edge.labelX" [attr.y]="edge.labelY">{{ edge.label }}</text> }
          }
          @for (node of nodes; track node.id) {
            <g class="diagram-node">
              <title>{{ node.label }}</title>
              @if (node.shape === 'diamond') {
                <polygon [attr.points]="diamondPoints(node)"></polygon>
              } @else if (node.shape === 'circle') {
                <ellipse [attr.cx]="node.x" [attr.cy]="node.y" [attr.rx]="nodeWidth * 0.37" [attr.ry]="nodeHeight / 2"></ellipse>
              } @else {
                <rect [attr.x]="node.x - nodeWidth / 2" [attr.y]="node.y - nodeHeight / 2"
                  [attr.width]="nodeWidth" [attr.height]="nodeHeight"
                  [attr.rx]="node.shape === 'round' ? 24 : 10"></rect>
              }
              <text [attr.x]="node.x" [attr.y]="node.y">{{ node.label }}</text>
            </g>
          }
        </svg>
      </div>
    } @else {
      <p class="diagram-fallback">This diagram uses syntax that the interactive renderer cannot display.</p>
    }
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .diagram-toolbar { display: flex; justify-content: flex-end; align-items: center; gap: 5px; margin-bottom: 5px; color: #64748b; font-size: 10px; }
    .diagram-toolbar span { margin-right: auto; }
    .diagram-toolbar button { min-width: 28px; min-height: 25px; padding: 2px 6px; border: 1px solid rgba(100,116,139,.25); border-radius: 6px; background: transparent; color: inherit; cursor: pointer; font: inherit; }
    .diagram-scroll { max-width: 100%; max-height: 340px; overflow: auto; }
    .diagram-svg { display: block; overflow: visible; }
    .diagram-edge { fill: none; stroke: #008ff5; stroke-width: 2.5; }
    marker path { fill: #008ff5; }
    .diagram-edge-label { fill: #0756b5; font: 600 12px/1.2 "Segoe UI", "Noto Sans Tamil", "Latha", "Nirmala UI", sans-serif; text-anchor: middle; paint-order: stroke; stroke: #fff; stroke-width: 5px; stroke-linejoin: round; }
    .diagram-node rect, .diagram-node polygon { fill: #fff; stroke: #00b5b8; stroke-width: 2; }
    .diagram-node text { fill: #061b40; font: 600 13px/1.2 "Segoe UI", "Noto Sans Tamil", "Latha", "Nirmala UI", sans-serif; text-anchor: middle; dominant-baseline: middle; pointer-events: none; }
    .diagram-fallback { color: #64748b; font-size: 12px; }
    :host-context(.viewer-dark) .diagram-edge-label { fill: #d7e3f5; stroke: #142746; }
    :host-context(.viewer-dark) .diagram-node rect, :host-context(.viewer-dark) .diagram-node polygon { fill: #112848; stroke: #73b5ff; }
    :host-context(.viewer-dark) .diagram-node text { fill: #eef6ff; }
  `]
})
export class PresentationDiagramComponent {
  private static nextInstance = 0;
  readonly markerId = `diagram-arrow-${++PresentationDiagramComponent.nextInstance}`;
  readonly nodeWidth = 180;
  readonly nodeHeight = 62;
  zoom = 1;
  diagramWidth = 900;
  diagramHeight = 440;
  nodes: DiagramNode[] = [];
  edges: DiagramEdge[] = [];
  viewBox = '0 0 900 440';
  ariaLabel = 'Presentation diagram';

  @Input() set mermaid(value: string | undefined) {
    this.buildGraph(value ?? '');
  }

  diamondPoints(node: DiagramNode) {
    const halfWidth = this.nodeWidth / 2;
    const halfHeight = this.nodeHeight / 2;
    return `${node.x},${node.y - halfHeight} ${node.x + halfWidth},${node.y} ${node.x},${node.y + halfHeight} ${node.x - halfWidth},${node.y}`;
  }

  adjustZoom(amount: number) {
    this.zoom = Math.min(2, Math.max(0.6, this.zoom + amount));
  }

  resetZoom() {
    this.zoom = 1;
  }

  private buildGraph(source: string) {
    this.zoom = 1;
    const lines = source.split(/\r?\n/).map(line => line.trim())
      .filter(line => line && !line.startsWith('%%') &&
        !/^(flowchart|graph|subgraph|end|classDef|style|linkStyle|click)\b/i.test(line));
    const nodePattern = /([A-Za-z0-9_-]+)\s*(\[\[[^\]]+\]\]|\[[^\]]+\]|\{\{[^}]+\}\}|\{[^}]+\}|\(\([^)]+\)\)|\(\[[^\]]+\]\)|\([^)]*\))?/g;
    const edgePattern = /(-->|---|-.->|==>|--o|--x)(?:\|([^|]*)\|)?/g;
    const nodeMap = new Map<string, Omit<DiagramNode, 'rank' | 'x' | 'y'>>();
    const pairs: { from: string; to: string; label: string }[] = [];

    for (const line of lines) {
      const tokens: ({ kind: 'node'; id: string; label: string; shape: DiagramNode['shape'] } | { kind: 'edge'; label: string })[] = [];
      let offset = 0;
      while (offset < line.length) {
        edgePattern.lastIndex = offset;
        nodePattern.lastIndex = offset;
        const edge = edgePattern.exec(line);
        const node = nodePattern.exec(line);
        if (!edge && !node) break;
        if (edge && (!node || edge.index <= node.index)) {
          tokens.push({ kind: 'edge', label: edge[2]?.trim() ?? '' });
          offset = edgePattern.lastIndex;
        } else if (node) {
          const parsed = this.parseNode(node[1], node[2] ?? '');
          if (!nodeMap.has(parsed.id) || Boolean(node[2])) nodeMap.set(parsed.id, parsed);
          tokens.push({ kind: 'node', ...parsed });
          offset = nodePattern.lastIndex;
        }
      }

      for (let index = 0; index < tokens.length - 2; index++) {
        const from = tokens[index];
        const edge = tokens[index + 1];
        const to = tokens[index + 2];
        if (from.kind === 'node' && edge.kind === 'edge' && to.kind === 'node') {
          pairs.push({ from: from.id, to: to.id, label: edge.label });
        }
      }
    }

    if (!nodeMap.size) {
      this.nodes = [];
      this.edges = [];
      return;
    }

    const direction = /\b(?:flowchart|graph)\s+(LR|RL|TB|TD|BT)\b/i.exec(source)?.[1]?.toUpperCase() ?? 'TD';
    const horizontal = direction === 'LR' || direction === 'RL';
    const reverse = direction === 'RL' || direction === 'BT';
    const ranks = new Map<string, number>();
    const outgoing = new Map<string, string[]>();
    const incomingCount = new Map<string, number>();
    for (const id of nodeMap.keys()) {
      outgoing.set(id, []);
      incomingCount.set(id, 0);
    }
    for (const edge of pairs) {
      if (!nodeMap.has(edge.from) || !nodeMap.has(edge.to)) continue;
      outgoing.get(edge.from)!.push(edge.to);
      incomingCount.set(edge.to, (incomingCount.get(edge.to) ?? 0) + 1);
    }
    const queue = [...nodeMap.keys()].filter(id => (incomingCount.get(id) ?? 0) === 0);
    for (const id of queue) ranks.set(id, 0);
    if (!queue.length) {
      queue.push(nodeMap.keys().next().value as string);
      ranks.set(queue[0], 0);
    }
    for (let cursor = 0; cursor < queue.length; cursor++) {
      const id = queue[cursor];
      for (const target of outgoing.get(id) ?? []) {
        if (ranks.has(target)) continue;
        ranks.set(target, (ranks.get(id) ?? 0) + 1);
        queue.push(target);
      }
    }
    for (const id of nodeMap.keys()) if (!ranks.has(id)) ranks.set(id, 0);

    const grouped = new Map<number, string[]>();
    for (const id of nodeMap.keys()) {
      const rank = ranks.get(id) ?? 0;
      grouped.set(rank, [...(grouped.get(rank) ?? []), id]);
    }
    const maxRank = Math.max(0, ...grouped.keys());
    const maxCount = Math.max(1, ...[...grouped.values()].map(group => group.length));
    const width = horizontal ? Math.max(900, (maxRank + 1) * 250 + 100) : Math.max(900, maxCount * 220 + 100);
    const height = horizontal ? Math.max(360, maxCount * 110 + 100) : Math.max(360, (maxRank + 1) * 125 + 100);
    this.viewBox = `0 0 ${width} ${height}`;
    this.diagramWidth = width;
    this.diagramHeight = height;
    this.nodes = [...nodeMap.values()].map(node => {
      const rank = ranks.get(node.id) ?? 0;
      const group = grouped.get(rank) ?? [];
      const place = group.indexOf(node.id);
      const primary = 60 + (reverse ? maxRank - rank : rank) * 250;
      const secondary = 55 + place * 110;
      return {
        ...node, rank,
        x: horizontal ? primary : 60 + (width - 120) * (place + 1) / (group.length + 1),
        y: horizontal ? secondary : primary
      };
    });
    const positions = new Map(this.nodes.map(node => [node.id, node]));
    this.edges = pairs.flatMap(edge => {
      const from = positions.get(edge.from);
      const to = positions.get(edge.to);
      if (!from || !to) return [];
      const startX = horizontal ? from.x + (reverse ? -1 : 1) * this.nodeWidth / 2 : from.x;
      const startY = horizontal ? from.y : from.y + (reverse ? -1 : 1) * this.nodeHeight / 2;
      const endX = horizontal ? to.x + (reverse ? 1 : -1) * this.nodeWidth / 2 : to.x;
      const endY = horizontal ? to.y : to.y + (reverse ? 1 : -1) * this.nodeHeight / 2;
      const midpointX = (startX + endX) / 2;
      const midpointY = (startY + endY) / 2;
      return [{
        ...edge,
        path: horizontal
          ? `M ${startX} ${startY} C ${midpointX} ${startY}, ${midpointX} ${endY}, ${endX} ${endY}`
          : `M ${startX} ${startY} C ${startX} ${midpointY}, ${endX} ${midpointY}, ${endX} ${endY}`,
        labelX: midpointX,
        labelY: midpointY - 7
      }];
    });
    this.ariaLabel = `Branching ${horizontal ? 'left-to-right' : 'top-to-bottom'} ${this.nodes.length}-node diagram`;
  }

  private parseNode(id: string, rawShape: string): Omit<DiagramNode, 'rank' | 'x' | 'y'> {
    const shape = rawShape.trim();
    const label = shape.startsWith('{{') ? shape.slice(2, -2) :
      shape.startsWith('[[') ? shape.slice(2, -2) :
        shape.startsWith('([') ? shape.slice(2, -2) :
        shape.startsWith('[(') ? shape.slice(2, -2) :
          shape.startsWith('((') ? shape.slice(2, -2) :
            shape.startsWith('[') || shape.startsWith('{') || shape.startsWith('(') ? shape.slice(1, -1) : id;
    return {
      id,
      label: label.replace(/<br\s*\/?>/gi, ' ').replace(/&quot;/g, '"').trim() || id,
      shape: shape.startsWith('{') ? 'diamond' :
        shape.startsWith('((') ? 'circle' :
          shape.startsWith('(') ? 'round' : 'rect'
    };
  }
}
