import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-presentation-table',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (table) {
      <div class="table-scroll">
        <table>
          <thead><tr>@for (header of table.headers; track $index) { <th>{{ header }}</th> }</tr></thead>
          <tbody>
            @for (row of table.rows; track $index) {
              <tr>@for (cell of row; track $index) { <td>{{ cell }}</td> }</tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .table-scroll { max-width: 100%; overflow: auto; }
    table { width: 100%; border-collapse: collapse; color: #172b4d; font-size: 11px; }
    th, td { padding: 8px 10px; border-bottom: 1px solid rgba(100,116,139,.18); text-align: left; }
    th { background: rgba(37,99,235,.08); color: #142b52; font-weight: 750; }
    :host-context(.viewer-dark) table { color: #edf4ff; }
    :host-context(.viewer-dark) th { color: #edf4ff; background: rgba(112,196,255,.12); }
  `]
})
export class PresentationTableComponent {
  @Input() table?: { headers: string[]; rows: string[][] };
}
