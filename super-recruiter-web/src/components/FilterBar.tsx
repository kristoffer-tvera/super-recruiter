import { useState } from "react";
import { type PlayerStatus, type PlayerFilter, WOW_CLASSES, ALL_STATUS_VALUES } from "../types";
import { STATUS_LABELS, CLASS_COLORS } from "../constants";

interface FilterBarProps {
    filter: PlayerFilter;
    onChange: (filter: PlayerFilter) => void;
    onRefresh: () => void;
}

export default function FilterBar({ filter, onChange, onRefresh }: FilterBarProps) {
    const [expanded, setExpanded] = useState(false);

    const hasActiveFilters =
        (filter.statuses && filter.statuses.length > 0) ||
        (filter.classes && filter.classes.length > 0) ||
        filter.minItemLevel !== undefined ||
        filter.minMythicKills !== undefined;

    const handleClearFilters = () => {
        onChange({
            limit: filter.limit,
            offset: 0,
        });
        setExpanded(false);
    };

    const toggleClass = (cls: string) => {
        const lower = cls.toLowerCase();
        const current = filter.classes ?? [];
        const updated = current.includes(lower) ? current.filter((c) => c !== lower) : [...current, lower];
        onChange({ ...filter, classes: updated.length > 0 ? updated : undefined, offset: 0 });
    };

    const toggleStatus = (status: PlayerStatus) => {
        const current = filter.statuses ?? [];
        const updated = current.includes(status) ? current.filter((s) => s !== status) : [...current, status];
        onChange({ ...filter, statuses: updated.length > 0 ? updated : undefined, offset: 0 });
    };

    return (
        <div className="mb-3">
            {/* Collapsed header with chips */}
            <div className="d-flex align-items-center gap-2 flex-wrap">
                <button type="button" className="btn btn-sm btn-outline-secondary d-flex align-items-center gap-2" onClick={() => setExpanded(!expanded)} aria-expanded={expanded}>
                    {expanded ? "▼" : "▶"} Filters
                </button>

                {/* Active filter chips */}
                {(filter.statuses ?? []).map((status) => (
                    <span key={`status-${status}`} className="badge bg-secondary d-flex align-items-center gap-1">
                        {STATUS_LABELS[status]}
                        <button
                            type="button"
                            className="btn-close btn-close-white p-0 ms-1"
                            aria-label="Remove"
                            onClick={() => toggleStatus(status)}
                            style={{ fontSize: "0.75rem" }}
                        />
                    </span>
                ))}

                {(filter.classes ?? []).map((cls) => (
                    <span key={`class-${cls}`} className="badge d-flex align-items-center gap-1" style={{ backgroundColor: CLASS_COLORS[cls] || "#6c757d" }}>
                        {cls.charAt(0).toUpperCase() + cls.slice(1)}
                        <button type="button" className="btn-close btn-close-white p-0 ms-1" aria-label="Remove" onClick={() => toggleClass(cls)} style={{ fontSize: "0.75rem" }} />
                    </span>
                ))}

                {filter.minItemLevel !== undefined && (
                    <span className="badge bg-info d-flex align-items-center gap-1">
                        {filter.minItemLevel}+ iLvl
                        <button
                            type="button"
                            className="btn-close btn-close-white p-0 ms-1"
                            aria-label="Remove"
                            onClick={() => onChange({ ...filter, minItemLevel: undefined, offset: 0 })}
                            style={{ fontSize: "0.75rem" }}
                        />
                    </span>
                )}

                {filter.minMythicKills !== undefined && (
                    <span className="badge bg-warning d-flex align-items-center gap-1">
                        {filter.minMythicKills}+ Kills
                        <button
                            type="button"
                            className="btn-close btn-close-white p-0 ms-1"
                            aria-label="Remove"
                            onClick={() => onChange({ ...filter, minMythicKills: undefined, offset: 0 })}
                            style={{ fontSize: "0.75rem" }}
                        />
                    </span>
                )}

                <button className="btn btn-sm btn-outline-secondary ms-auto" onClick={onRefresh}>
                    Refresh
                </button>
            </div>

            {/* Expanded filter panel */}
            {expanded && (
                <div className="card mt-2 p-3">
                    <div className="row">
                        {/* Statuses */}
                        <div className="col-md-3">
                            <h6 className="fw-bold mb-2">Statuses</h6>
                            <div className="d-flex flex-column gap-2">
                                {ALL_STATUS_VALUES.map((status) => (
                                    <div key={`status-${status}`} className="form-check">
                                        <input
                                            className="form-check-input"
                                            type="checkbox"
                                            id={`status-${status}`}
                                            checked={(filter.statuses ?? []).includes(status)}
                                            onChange={() => toggleStatus(status)}
                                        />
                                        <label className="form-check-label" htmlFor={`status-${status}`}>
                                            {STATUS_LABELS[status]}
                                        </label>
                                    </div>
                                ))}
                            </div>
                        </div>

                        {/* Classes */}
                        <div className="col-md-4">
                            <h6 className="fw-bold mb-2">Classes</h6>
                            <div className="row g-2">
                                {WOW_CLASSES.map((cls) => {
                                    const lower = cls.toLowerCase();
                                    return (
                                        <div key={cls} className="col-6">
                                            <div className="form-check">
                                                <input
                                                    className="form-check-input"
                                                    type="checkbox"
                                                    id={`class-${lower.replace(" ", "-")}`}
                                                    checked={(filter.classes ?? []).includes(lower)}
                                                    onChange={() => toggleClass(cls)}
                                                />
                                                <label className="form-check-label" htmlFor={`class-${lower.replace(" ", "-")}`}>
                                                    {cls}
                                                </label>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        </div>

                        {/* Numeric filters */}
                        <div className="col-md-5">
                            <h6 className="fw-bold mb-2">Other Filters</h6>
                            <div className="mb-3">
                                <label htmlFor="minItemLevel" className="form-label">
                                    Minimum Item Level
                                </label>
                                <input
                                    id="minItemLevel"
                                    type="number"
                                    className="form-control form-control-sm"
                                    placeholder="e.g., 450"
                                    step="0.1"
                                    value={filter.minItemLevel ?? ""}
                                    onChange={(e) =>
                                        onChange({
                                            ...filter,
                                            minItemLevel: e.target.value ? parseFloat(e.target.value) : undefined,
                                            offset: 0,
                                        })
                                    }
                                />
                            </div>
                            <div className="mb-3">
                                <label htmlFor="minMythicKills" className="form-label">
                                    Minimum Mythic Boss Kills
                                </label>
                                <input
                                    id="minMythicKills"
                                    type="number"
                                    className="form-control form-control-sm"
                                    placeholder="e.g., 10"
                                    step="1"
                                    value={filter.minMythicKills ?? ""}
                                    onChange={(e) =>
                                        onChange({
                                            ...filter,
                                            minMythicKills: e.target.value ? parseInt(e.target.value, 10) : undefined,
                                            offset: 0,
                                        })
                                    }
                                />
                            </div>
                        </div>
                    </div>

                    <div className="d-flex gap-2 mt-3">
                        {hasActiveFilters && (
                            <button type="button" className="btn btn-sm btn-outline-danger" onClick={handleClearFilters}>
                                Clear Filters
                            </button>
                        )}
                        <button type="button" className="btn btn-sm btn-outline-secondary ms-auto" onClick={() => setExpanded(false)}>
                            Close
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}
