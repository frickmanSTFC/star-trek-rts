"""Which techs a faction can research, shared by the tech tree and hazards pages."""


def reachable(techs, f):
    """Techs a faction can research: allowed for it, with every prerequisite also reachable.
    techs maps tech id -> tech entry from data/tech_tree.json."""
    ok = {}

    def can(tid):
        if tid not in ok:
            t = techs[tid]
            ok[tid] = (not t.get("faction_only") or f in t["faction_only"]) and all(can(p) for p in t["prereqs"])
        return ok[tid]
    return {tid for tid in techs if can(tid)}
