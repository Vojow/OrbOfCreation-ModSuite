from orbtools.gamedata.typetrees import MONO_HEADER, strip_exact_monobehaviour_header


def flat_nodes(names: tuple[str, ...]) -> list[dict[str, object]]:
    nodes: list[dict[str, object]] = [{"m_Level": 0, "m_Name": "Root"}]
    for name in names:
        nodes.extend(
            [
                {"m_Level": 1, "m_Name": name},
                {"m_Level": 2, "m_Name": f"{name}Child"},
            ]
        )
    return nodes


def test_strips_only_exact_header_prefix() -> None:
    nodes = flat_nodes((*MONO_HEADER, "payload"))
    stripped = strip_exact_monobehaviour_header(nodes)
    assert [node["m_Name"] for node in stripped] == ["Root", "payload", "payloadChild"]


def test_preserves_partial_or_changed_prefix() -> None:
    nodes = flat_nodes(("m_GameObject", "m_Enabled", "payload"))
    assert strip_exact_monobehaviour_header(nodes) == nodes
