import { type PlayerStatus, type PlayerFilter } from "../types";
import { STATUS_LABELS, CLASS_ICONS } from "../constants";

interface FilterBarProps {
  filter: PlayerFilter;
  onChange: (filter: PlayerFilter) => void;
  onRefresh: () => void;
}

export default function FilterBar({
  filter,
  onChange,
  onRefresh,
}: FilterBarProps) {
  return (
    <div className="d-flex gap-2 flex-wrap align-items-center mb-3">
      <button
        className={`btn btn-sm ${filter.status === undefined ? "btn-primary" : "btn-outline-primary"}`}
        onClick={() => onChange({ ...filter, status: undefined })}
      >
        All
      </button>
      {Object.entries(STATUS_LABELS).map(([key, label]) => {
        const s = Number(key) as PlayerStatus;
        return (
          <button
            key={key}
            className={`btn btn-sm ${filter.status === s ? "btn-primary" : "btn-outline-primary"}`}
            onClick={() => onChange({ ...filter, status: s })}
          >
            {label}
          </button>
        );
      })}
      <select
        className="form-select form-select-sm ms-2"
        style={{ width: "auto", minWidth: "120px" }}
        value={filter.playerClass ?? ""}
        onChange={(e) =>
          onChange({ ...filter, playerClass: e.target.value || undefined })
        }
      >
        <option value="">All Classes</option>
        {Object.keys(CLASS_ICONS).map((cls) => (
          <option key={cls} value={cls}>
            {cls.charAt(0).toUpperCase() + cls.slice(1)}
          </option>
        ))}
      </select>
      <button
        className="btn btn-sm btn-outline-secondary ms-auto"
        onClick={onRefresh}
      >
        Refresh
      </button>
    </div>
  );
}
