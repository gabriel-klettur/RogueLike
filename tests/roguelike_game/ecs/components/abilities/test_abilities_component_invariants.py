from roguelike_game.ecs.components.abilities.lightning_model import LightningModel


def test_lightning_model_points_and_lifetime_invariants():
    start = (0.0, 0.0)
    end = (10.0, 0.0)
    segments = 6
    offset = 3
    lifetime = 4

    model = LightningModel(start, end, segments=segments, offset=offset, lifetime=lifetime)

    # Points invariants
    assert model.points[0] == start
    assert model.points[-1] == end
    assert len(model.points) == segments + 1  # start + (segments-1) mids + end

    # Lifetime invariants: monotonically non-increasing
    prev = model.lifetime
    for _ in range(lifetime):
        model.update()
        assert model.lifetime == prev - 1
        prev = model.lifetime

    assert model.is_finished() is True
