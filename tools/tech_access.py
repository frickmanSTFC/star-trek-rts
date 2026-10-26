"""Which techs a faction can research, shared by the tech tree and hazards pages."""


def reachable(techs, f, rules=None):
    """Techs a faction can get: allowed for it, with every prerequisite also reachable.
    techs maps tech id -> tech entry from data/tech_tree.json; rules is its faction_rules. A faction that
    doesn't research (the Borg) can get every tech, other factions' faction-only ones included, by
    assimilating it or combining its prerequisites."""
    if rules and rules.get(f, {}).get("research") is False:
        return set(techs)
    ok = {}

    def can(tid):
        if tid not in ok:
            t = techs[tid]
            ok[tid] = (not t.get("faction_only") or f in t["faction_only"]) and all(can(p) for p in t["prereqs"])
        return ok[tid]
    return {tid for tid in techs if can(tid)}
